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
using System.Security.Cryptography;


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
            _logger.LogInformation("Starting chunk session initialization for EntityId: {EntityId}, FileName: {FileName}", request.EntityId, request.FileName);

            var session = new ChunkSession
            {
                Id = Guid.NewGuid(),
                EntityId = request.EntityId,
                FileName = SanitizeFileName(request.FileName),
                TotalChunks = request.TotalChunks,
                TotalSize = request.TotalSize,
                ExpiresAt = DateTime.UtcNow.AddHours(6)
            };

            _logger.LogDebug("Initialized session details: {@Session}", session);

            var redisKey = $"chunkSession:{session.Id}";
            bool redisSetResult = await _redis.GetDatabase().StringSetAsync(
                redisKey,
                JsonSerializer.Serialize(session),
                TimeSpan.FromHours(6));

            _logger.LogInformation("Stored chunk session {SessionId} in Redis with key {RedisKey}. Set result: {Result}", session.Id, redisKey, redisSetResult);

            return session;
        }

        public async Task ProcessChunkAsync(Guid sessionId, int chunkIndex, Stream chunkData)
{
    _logger.LogInformation("Processing chunk {ChunkIndex} for session {SessionId}. Chunk size: {ChunkSize} bytes", 
        chunkIndex, sessionId, chunkData.Length);

    await using var lockHandle = await _lockProvider.AcquireLockAsync(
        $"chunk:{sessionId}",
        TimeSpan.FromSeconds(30));
    
    var session = await GetSessionAsync(sessionId);
    
    _logger.LogInformation("Validating chunk {ChunkIndex} for session {SessionId}", chunkIndex, sessionId);
    ValidateChunk(session, chunkIndex, chunkData.Length);

    // Create a dedicated memory stream and fully copy all data to it
    using var memoryStream = new MemoryStream();
    await chunkData.CopyToAsync(memoryStream);
    
    // Make sure everything is flushed to the memory buffer
    await memoryStream.FlushAsync();
    
    // IMPORTANT: Reset position to beginning before using it as a source
    memoryStream.Position = 0;
    
    // Log the actual chunk size before upload for debugging
    _logger.LogInformation("Prepared chunk {ChunkIndex} for upload. Size in memory: {MemoryStreamLength} bytes", 
        chunkIndex, memoryStream.Length);

    var chunkKey = $"{sessionId}/{chunkIndex}";
    _logger.LogInformation("Uploading chunk {ChunkIndex} for session {SessionId} to storage with key {ChunkKey}", 
        chunkIndex, sessionId, chunkKey);

    // Verify data is intact before upload by calculating a checksum
    memoryStream.Position = 0;
    var checksumBytes = new byte[memoryStream.Length];
    await memoryStream.ReadAsync(checksumBytes, 0, checksumBytes.Length);
    var checksum = Convert.ToHexString(SHA256.HashData(checksumBytes));
    _logger.LogDebug("Chunk checksum before upload: {Checksum}", checksum);
    
    // Reset position again before upload
    memoryStream.Position = 0;

    await _storageService.UploadAsync(
        memoryStream,
        "temp-chunks",
        chunkKey,
        "application/octet-stream",
        new Dictionary<string, string>
        {
            ["sessionId"] = sessionId.ToString(),
            ["chunkIndex"] = chunkIndex.ToString(),
            ["checksum"] = checksum,
            ["originalSize"] = memoryStream.Length.ToString()
        });

    _logger.LogInformation("Uploaded chunk {ChunkIndex} to {Bucket}/{ChunkKey}", 
        chunkIndex, "temp-chunks", chunkKey);

    session.UploadedChunks.Add(chunkIndex);
    
    await UpdateSessionAsync(session);
    _logger.LogInformation("Session {SessionId} updated in Redis after processing chunk {ChunkIndex}", 
        sessionId, chunkIndex);
}

        public async Task<ChunkSession> GetSessionAsync(Guid sessionId)
        {
            _logger.LogInformation("Retrieving chunk session {SessionId} from Redis", sessionId);
            var redisKey = $"chunkSession:{sessionId}";
            var raw = await _redis.GetDatabase().StringGetAsync(redisKey);
            if (raw.IsNullOrEmpty)
            {
                _logger.LogWarning("Chunk session {SessionId} not found in Redis", sessionId);
                throw new InvalidOperationException($"Chunk session {sessionId} not found in Redis.");
            }

            var session = JsonSerializer.Deserialize<ChunkSession>(
                raw.ToString(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (session == null)
            {
                _logger.LogError("Deserialization of chunk session {SessionId} returned null", sessionId);
                throw new InvalidOperationException($"Deserialization of chunk session {sessionId} returned null.");
            }

            _logger.LogInformation("Successfully retrieved chunk session {SessionId} from Redis", sessionId);
            return session;
        }

        public async Task<Content> CompleteSessionAsync(Guid sessionId, string userId)
        {
            _logger.LogInformation("Initiating completion of chunk session {SessionId} for user {UserId}", sessionId, userId);

            await using var lockHandle = await _lockProvider.AcquireLockAsync(
                $"complete:{sessionId}",
                TimeSpan.FromMinutes(10));
            _logger.LogDebug("Acquired completion lock for session {SessionId}", sessionId);

            var session = await GetSessionAsync(sessionId);
            _logger.LogInformation("Validating session completion for session {SessionId}", sessionId);
            ValidateSessionCompletion(session);

            _logger.LogInformation("Assembling chunks for session {SessionId}", sessionId);
            var tempFile = await AssembleChunksAsync(session);

            _logger.LogInformation("Validating final assembled file for session {SessionId}", sessionId);
            await ValidateFinalFileAsync(tempFile);

            _logger.LogInformation("Storing final file for session {SessionId} to permanent storage", sessionId);
            var finalContent = await StoreFinalFileAsync(tempFile, session, userId);
            _logger.LogInformation("Final file stored successfully with ContentId: {ContentId} for session {SessionId}", finalContent.Id, sessionId);

            _logger.LogInformation("Cleaning up temporary resources for session {SessionId}", sessionId);
            await CleanupResourcesAsync(session);

            _logger.LogInformation("Chunk session {SessionId} completed successfully", sessionId);
            return finalContent;
        }

        private async Task ValidateFinalFileAsync(string tempFilePath)
        {
            _logger.LogInformation("Scanning assembled file for viruses: {TempFilePath}", tempFilePath);
            await using var fileStream = File.OpenRead(tempFilePath);
            var scanResult = await _virusScanner.ScanAsync(fileStream);
            if (scanResult.IsMalicious)
            {
                _logger.LogError("Virus scan detected malicious content in file: {TempFilePath}", tempFilePath);
                throw new SecurityException("Final file contains malicious content");
            }
            _logger.LogInformation("Virus scan passed for file: {TempFilePath}", tempFilePath);
        }

        private string SanitizeFileName(string fileName)
        {
            _logger.LogInformation("Sanitizing file name: {FileName}", fileName);
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName
                .Replace(" ", "_")
                .Where(c => !invalidChars.Contains(c))
                .ToArray());
            _logger.LogInformation("Sanitized file name: {SanitizedFileName}", sanitized);
            return sanitized;
        }

        private async Task UpdateSessionAsync(ChunkSession session)
        {
            _logger.LogInformation("Updating chunk session {SessionId} in Redis", session.Id);
            var redisKey = $"chunkSession:{session.Id}";
            await _redis.GetDatabase().StringSetAsync(redisKey, JsonSerializer.Serialize(session));
            _logger.LogInformation("Chunk session {SessionId} updated successfully", session.Id);
        }

        private void ValidateChunk(ChunkSession session, int chunkIndex, long chunkSize)
        {
            _logger.LogInformation("Validating chunk {ChunkIndex} with size {ChunkSize} for session {SessionId}", chunkIndex, chunkSize, session.Id);

            if (chunkIndex < 0 || chunkIndex >= session.TotalChunks)
            {
                _logger.LogError("Invalid chunk index {ChunkIndex} for session {SessionId}", chunkIndex, session.Id);
                throw new ArgumentOutOfRangeException(nameof(chunkIndex), $"Invalid chunk index {chunkIndex} for session {session.Id}");
            }

            if (chunkSize <= 0)
            {
                _logger.LogError("Chunk size {ChunkSize} is not greater than zero for session {SessionId}, chunk {ChunkIndex}", chunkSize, session.Id, chunkIndex);
                throw new ArgumentException("Chunk size must be greater than zero", nameof(chunkSize));
            }

            var expectedChunkSize = session.TotalSize / session.TotalChunks;
            var tolerance = 1024 * 1024; // 1MB tolerance
            if (Math.Abs(chunkSize - expectedChunkSize) > tolerance && chunkIndex != session.TotalChunks - 1)
            {
                _logger.LogError("Chunk {ChunkIndex} size {ChunkSize} deviates too much from expected {ExpectedChunkSize} for session {SessionId}", chunkIndex, chunkSize, expectedChunkSize, session.Id);
                throw new InvalidDataException($"Chunk {chunkIndex} size {chunkSize} deviates too much from expected {expectedChunkSize}");
            }

            _logger.LogInformation("Chunk {ChunkIndex} validated successfully for session {SessionId}", chunkIndex, session.Id);
        }

        private void ValidateSessionCompletion(ChunkSession session)
        {
            _logger.LogInformation("Validating session completion for session {SessionId}. Uploaded chunks: {UploadedChunks}/{TotalChunks}", session.Id, session.UploadedChunks.Count, session.TotalChunks);

            if (session.UploadedChunks.Count != session.TotalChunks)
            {
                _logger.LogError("Session {SessionId} is incomplete. Uploaded {UploadedChunks} of {TotalChunks} chunks", session.Id, session.UploadedChunks.Count, session.TotalChunks);
                throw new InvalidOperationException($"Session {session.Id} incomplete. Received {session.UploadedChunks.Count}/{session.TotalChunks} chunks");
            }

            if (DateTime.UtcNow > session.ExpiresAt)
            {
                _logger.LogError("Session {SessionId} has expired at {ExpiresAt}. Current time: {CurrentTime}", session.Id, session.ExpiresAt, DateTime.UtcNow);
                throw new TimeoutException($"Session {session.Id} has expired");
            }

            _logger.LogInformation("Session {SessionId} passed completion validation", session.Id);
        }

        private async Task<string> AssembleChunksAsync(ChunkSession session)
        {
            _logger.LogInformation("Starting assembly of chunks for session {SessionId}", session.Id);
            var tempDir = Path.Combine(Path.GetTempPath(), "chunk_assemblies");
            Directory.CreateDirectory(tempDir);
            _logger.LogDebug("Temporary directory for chunk assemblies: {TempDir}", tempDir);

            var tempFile = Path.Combine(tempDir, $"{session.Id}-assembled");
            _logger.LogInformation("Assembled file will be created at {TempFile}", tempFile);

            // Wrap the loop within the using block to ensure the stream is disposed before checking the size
            await using (var outputStream = File.Create(tempFile))
            {
                for (int i = 0; i < session.TotalChunks; i++)
                {
                    // Fix: Use the same path format as in ProcessChunkAsync
                    var chunkKey = $"{session.Id}/{i}";
                    _logger.LogInformation("Attempting to download {Bucket}/{ChunkKey}", "temp-chunks", chunkKey);

                    await using var chunkStream = await _storageService.DownloadAsync("temp-chunks", chunkKey);
                    _logger.LogInformation("Copying chunk {ChunkIndex} into assembled file for session {SessionId}", i, session.Id);
                    await chunkStream.CopyToAsync(outputStream);
                    _logger.LogInformation("Chunk {ChunkIndex} assembled successfully for session {SessionId}", i, session.Id);
                }
                
                // Ensure all data is flushed to disk
                await outputStream.FlushAsync();
            } 
    
            var fileInfo = new FileInfo(tempFile);
            _logger.LogInformation("Assembled file size: {FileSize} bytes. Expected size: {TotalSize} bytes for session {SessionId}", fileInfo.Length, session.TotalSize, session.Id);

            if (fileInfo.Length != session.TotalSize)
            {
                _logger.LogError("Assembled file size mismatch for session {SessionId}. Actual: {ActualSize}, Expected: {ExpectedSize}", session.Id, fileInfo.Length, session.TotalSize);
                throw new InvalidDataException($"Assembled file size {fileInfo.Length} doesn't match expected {session.TotalSize}");
            }

            _logger.LogInformation("All chunks assembled successfully for session {SessionId}", session.Id);
            return tempFile;
        }

        private async Task<Content> StoreFinalFileAsync(string tempFilePath, ChunkSession session, string userId)
        {
            _logger.LogInformation("Storing final file for session {SessionId}. Temp file path: {TempFilePath}", session.Id, tempFilePath);
            var fileExtension = Path.GetExtension(session.FileName);
            var contentTypeEnum = GetContentType(fileExtension);
            var mimeType = GetMimeType(contentTypeEnum);
            var finalObjectName = $"{userId}/{session.EntityId}/{session.Id}{fileExtension}";
            _logger.LogInformation("Final object name: {FinalObjectName}, MIME type: {MimeType}", finalObjectName, mimeType);

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

            _logger.LogInformation("Final file uploaded to bucket {BucketName} with object name {ObjectName}", uploadResult.BucketName, uploadResult.ObjectName);

            var content = new Content
            {
                Id = Guid.NewGuid(),
                EntityId = session.EntityId,
                EntityType = "Content",
                ContentType = contentTypeEnum,
                BucketName = uploadResult.BucketName,
                ObjectName = uploadResult.ObjectName,
                CreatedAt = DateTime.UtcNow
            };

            _logger.LogInformation("Final content created with ID {ContentId}", content.Id);
            return content;
        }

        private async Task CleanupResourcesAsync(ChunkSession session)
        {
            _logger.LogInformation("Cleaning up temporary resources for session {SessionId}", session.Id);
            try
            {
                for (int i = 0; i < session.TotalChunks; i++)
                {
                    // Fix: Use the same path format as in ProcessChunkAsync
                    var chunkKey = $"{session.Id}/{i}";
                    _logger.LogInformation("Deleting temporary chunk file with key {ChunkKey}", chunkKey);
                    await _storageService.DeleteAsync("temp-chunks", chunkKey);
                }

                var redisKey = $"chunkSession:{session.Id}";
                _logger.LogInformation("Deleting Redis key {RedisKey} for session {SessionId}", redisKey, session.Id);
                await _redis.GetDatabase().KeyDeleteAsync(redisKey);

                _logger.LogInformation("Cleanup of resources completed for session {SessionId}", session.Id);
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
            _logger.LogInformation("Determining ContentType for file extension {FileExtension}", fileExtension);
            var contentType = fileExtension.ToLower() switch
            {
                ".jpg" or ".jpeg" or ".png" or ".gif" => ContentType.Image,
                ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" => ContentType.Document,
                ".mp4" or ".mov" or ".avi" or ".wmv" => ContentType.Video,
                _ => ContentType.Other
            };
            _logger.LogInformation("Determined ContentType: {ContentType} for file extension {FileExtension}", contentType, fileExtension);
            return contentType;
        }

        /// <summary>
        /// Converts our ContentType enum value into a valid MIME type string.
        /// </summary>
        private string GetMimeType(ContentType contentType)
        {
            _logger.LogInformation("Determining MIME type for ContentType {ContentType}", contentType);
            var mimeType = contentType switch
            {
                ContentType.Image => "image/jpeg",
                ContentType.Document => "application/pdf",
                ContentType.Video => "video/mp4",
                _ => "application/octet-stream"
            };
            _logger.LogInformation("Determined MIME type: {MimeType} for ContentType {ContentType}", mimeType, contentType);
            return mimeType;
        }
    }
}