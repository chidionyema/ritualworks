using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading.Tasks;
using haworks.Contracts;
using haworks.Dto;
using haworks.Db;
using haworks.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RedLockNet;
using RedLockNet.SERedis;
using StackExchange.Redis;
using haworks.Infrastructure;

namespace haworks.Services
{
    public class ChunkedUploadService : IChunkedUploadService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDistributedLockProvider _lockProvider;
        private readonly IContentStorageService _storageService;
        private readonly IVirusScanner _virusScanner;
        private readonly ILogger<ChunkedUploadService> _logger;

        public ChunkedUploadService(
            IConnectionMultiplexer redis,
            IDistributedLockProvider lockProvider,
            IContentStorageService storageService,
            IVirusScanner virusScanner,
            ILogger<ChunkedUploadService> logger)
        {
            _redis = redis;
            _lockProvider = lockProvider;
            _storageService = storageService;
            _virusScanner = virusScanner;
            _logger = logger;
        }

        public async Task<ChunkSession> InitSessionAsync(ChunkSessionRequest request)
        {
            var session = new ChunkSession
            {
                Id = Guid.NewGuid(),
                EntityId = request.EntityId,
                FileName = SanitizeFileName(request.FileName),
                TotalChunks = request.TotalChunks,
                TotalSize = request.TotalSize,
                ExpiresAt = DateTime.UtcNow.AddHours(6)
            };

            var redisKey = $"chunkSession:{session.Id}";
            await _redis.GetDatabase().StringSetAsync(
                redisKey,
                JsonSerializer.Serialize(session),
                TimeSpan.FromHours(6));

            return session;
        }

        public async Task ProcessChunkAsync(Guid sessionId, int chunkIndex, Stream chunkData)
        {
            await using var lockHandle = await _lockProvider.AcquireLockAsync(
                $"chunk:{sessionId}",
                TimeSpan.FromSeconds(30));

            var session = await GetSessionAsync(sessionId);
            ValidateChunk(session, chunkIndex, chunkData.Length);

            var chunkKey = $"chunks/{sessionId}/{chunkIndex}";
            await _storageService.UploadAsync(
                chunkData,
                "temp-chunks",
                chunkKey,
                "application/octet-stream",
                new Dictionary<string, string>
                {
                    ["sessionId"] = sessionId.ToString(),
                    ["chunkIndex"] = chunkIndex.ToString()
                });

            session.UploadedChunks.Add(chunkIndex);
            await UpdateSessionAsync(session);
        }

        public async Task<ChunkSession> GetSessionAsync(Guid sessionId) 
        {
            var redisKey = $"chunkSession:{sessionId}";
            var raw = await _redis.GetDatabase().StringGetAsync(redisKey);
            if (raw.IsNullOrEmpty)
            {
                throw new InvalidOperationException($"Chunk session {sessionId} not found in Redis.");
            }

            var session = JsonSerializer.Deserialize<ChunkSession>(
                raw.ToString(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (session == null)
            {
                throw new InvalidOperationException($"Deserialization of chunk session {sessionId} returned null.");
            }
            return session;
        }

        public async Task<Content> CompleteSessionAsync(Guid sessionId, string userId)
        {
            await using var lockHandle = await _lockProvider.AcquireLockAsync(
                $"complete:{sessionId}",
                TimeSpan.FromMinutes(10));

            var session = await GetSessionAsync(sessionId);
            ValidateSessionCompletion(session);

            var tempFile = await AssembleChunksAsync(session);
            await ValidateFinalFileAsync(tempFile);

            var finalContent = await StoreFinalFileAsync(tempFile, session, userId);
            await CleanupResourcesAsync(session);

            return finalContent;
        }

        private async Task ValidateFinalFileAsync(string tempFilePath)
        {
            await using var fileStream = File.OpenRead(tempFilePath);
            var scanResult = await _virusScanner.ScanAsync(fileStream);
            if (scanResult.IsMalicious)
            {
                throw new SecurityException("Final file contains malicious content");
            }
        }

        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(fileName
                .Replace(" ", "_")
                .Where(c => !invalidChars.Contains(c))
                .ToArray());
        }

        private async Task UpdateSessionAsync(ChunkSession session)
        {
            var redisKey = $"chunkSession:{session.Id}";
            await _redis.GetDatabase().StringSetAsync(redisKey, JsonSerializer.Serialize(session));
        }

        private void ValidateChunk(ChunkSession session, int chunkIndex, long chunkSize)
        {
            if (chunkIndex < 0 || chunkIndex >= session.TotalChunks)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkIndex),
                    $"Invalid chunk index {chunkIndex} for session {session.Id}");
            }

            if (chunkSize <= 0)
            {
                throw new ArgumentException("Chunk size must be greater than zero", nameof(chunkSize));
            }

            var expectedChunkSize = session.TotalSize / session.TotalChunks;
            var tolerance = 1024 * 1024; // 1MB tolerance
            if (Math.Abs(chunkSize - expectedChunkSize) > tolerance &&
                chunkIndex != session.TotalChunks - 1)
            {
                throw new InvalidDataException(
                    $"Chunk {chunkIndex} size {chunkSize} deviates too much from expected {expectedChunkSize}");
            }
        }

        private void ValidateSessionCompletion(ChunkSession session)
        {
            if (session.UploadedChunks.Count != session.TotalChunks)
            {
                throw new InvalidOperationException(
                    $"Session {session.Id} incomplete. Received {session.UploadedChunks.Count}/{session.TotalChunks} chunks");
            }

            if (DateTime.UtcNow > session.ExpiresAt)
            {
                throw new TimeoutException($"Session {session.Id} has expired");
            }
        }

        private async Task<string> AssembleChunksAsync(ChunkSession session)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "chunk_assemblies");
            Directory.CreateDirectory(tempDir);
            var tempFile = Path.Combine(tempDir, $"{session.Id}-assembled");

            await using var outputStream = File.Create(tempFile);

            for (int i = 0; i < session.TotalChunks; i++)
            {
                var chunkKey = $"chunks/{session.Id}/{i}";
                await using var chunkStream = await _storageService.DownloadAsync("temp-chunks", chunkKey);
                await chunkStream.CopyToAsync(outputStream);
                _logger.LogInformation("Assembled chunk {ChunkIndex} for session {SessionId}", i, session.Id);
            }

            var fileInfo = new FileInfo(tempFile);
            if (fileInfo.Length != session.TotalSize)
            {
                throw new InvalidDataException(
                    $"Assembled file size {fileInfo.Length} doesn't match expected {session.TotalSize}");
            }

            return tempFile;
        }

        private async Task<Content> StoreFinalFileAsync(string tempFilePath, ChunkSession session, string userId)
        {
            var fileExtension = Path.GetExtension(session.FileName);
            // Get our ContentType enum value based on the file extension.
            var contentTypeEnum = GetContentType(fileExtension);
            // Convert our enum to a valid MIME type string.
            var mimeType = GetMimeType(contentTypeEnum);
            var finalObjectName = $"{userId}/{session.EntityId}/{session.Id}{fileExtension}";

            await using var fileStream = File.OpenRead(tempFilePath);

            var uploadResult = await _storageService.UploadAsync(
                fileStream,
                "final-content",
                finalObjectName,
                mimeType,
                new Dictionary<string, string>
                {
                    ["entityId"] = session.EntityId.ToString(),
                    ["userId"] = userId,
                    ["originalName"] = session.FileName
                });

            return new Content
            {
                Id = Guid.NewGuid(),
                EntityId = session.EntityId,
                EntityType = "Content",
                ContentType = contentTypeEnum,
                BucketName = uploadResult.BucketName,
                ObjectName = uploadResult.ObjectName,
                CreatedAt = DateTime.UtcNow
            };
        }

        private async Task CleanupResourcesAsync(ChunkSession session)
        {
            try
            {
                // Delete all temporary chunk files
                for (int i = 0; i < session.TotalChunks; i++)
                {
                    var chunkKey = $"chunks/{session.Id}/{i}";
                    await _storageService.DeleteAsync("temp-chunks", chunkKey);
                }

                // Delete session from Redis
                var redisKey = $"chunkSession:{session.Id}";
                await _redis.GetDatabase().KeyDeleteAsync(redisKey);

                _logger.LogInformation("Cleaned up resources for session {SessionId}", session.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up resources for session {SessionId}", session.Id);
            }
        }

        /// <summary>
        /// Maps a file extension to a ContentType enum value.
        /// </summary>
        private ContentType GetContentType(string fileExtension)
        {
            return fileExtension.ToLower() switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" => ContentType.Image,
                ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" => ContentType.Document,
                ".mp4" or ".mov" or ".avi" or ".wmv" => ContentType.Video,
                _ => ContentType.Other
            };
        }

        /// <summary>
        /// Converts our ContentType enum value into a valid MIME type string.
        /// </summary>
        private string GetMimeType(ContentType contentType)
        {
            return contentType switch
            {
                ContentType.Image => "image/jpeg",      // Default image MIME type
                ContentType.Document => "application/pdf", // Default document MIME type
                ContentType.Video => "video/mp4",         // Default video MIME type
                _ => "application/octet-stream"
            };
        }
    }
}
