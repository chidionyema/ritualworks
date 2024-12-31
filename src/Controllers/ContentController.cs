using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using haworks.Contracts;
using haworks.Db;
using haworks.Dto;
using haworks.Models;
using haworks.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace haworks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContentController : ControllerBase
    {
        private readonly haworksContext _db; // We can inline purchase logic here
        private readonly IContentRepository _contentRepository;
        private readonly IContentService _contentService;
        private readonly ILogger<ContentController> _logger;
        private readonly IConnectionMultiplexer _redis; // For chunk sessions if needed

        public ContentController(
            haworksContext db,
            IContentRepository contentRepository,
            IContentService contentService,
            ILogger<ContentController> logger,
            IConnectionMultiplexer redis = null
        )
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _contentRepository = contentRepository ?? throw new ArgumentNullException(nameof(contentRepository));
            _contentService = contentService ?? throw new ArgumentNullException(nameof(contentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redis = redis; // can be null if chunk approach not used
        }

        // ============= Single request upload (2-bucket) ==============

        [HttpPost("upload")]
        [Authorize]
        public async Task<ActionResult<List<ContentDto>>> UploadContent(
            [FromQuery] Guid entityId,
            [FromQuery] string entityType = "Product",
            [FromQuery] string name = "unnamed",
            [FromForm] List<IFormFile> content = null)
        {
            if (entityId == Guid.Empty)
                return BadRequest("entityId cannot be empty.");
            if (content == null || content.Count == 0)
                return BadRequest("No files provided.");

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var newContents = new List<Content>();

            try
            {
                foreach (var file in content)
                {
                    if (!_contentService.ValidateFile(file, out var error, out var isImage))
                    {
                        _logger.LogWarning("Invalid file: {Error}", error);
                        return BadRequest($"File {file.FileName} invalid: {error}");
                    }

                    var url = await _contentService.UploadFileAsync(file, entityId, userId);

                    // Use isImage (bool) to determine the ContentType
                    var cType = isImage ? ContentType.Image : ContentType.Asset;

                    newContents.Add(new Content
                    {
                        Id = Guid.NewGuid(),
                        EntityId = entityId,
                        EntityType = entityType,
                        ContentType = cType,
                        Url = url,
                        BlobName = Path.GetFileName(url),
                        CreatedAt = DateTime.UtcNow
                    });                               
                } 

                if (newContents.Any())
                {
                    await _contentRepository.AddContentsAsync(newContents);
                    await _contentRepository.SaveChangesAsync();
                }

                var dtos = newContents.Select(c => new ContentDto
                {
                    Id = c.Id,
                    EntityId = c.EntityId,
                    EntityType = c.EntityType,
                    Url = c.Url,
                    ContentType = c.ContentType
                }).ToList();

                return Ok(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading content for entity {EntityId}", entityId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error uploading content");
            }
        }

        // ============= Chunk approach for large files ==============

        [HttpPost("chunked-init")]
        [Authorize]
        public async Task<ActionResult<ChunkInitResponse>> ChunkedInit(
            [FromQuery] Guid entityId,
            [FromQuery] string entityType = "Product",
            [FromQuery] string fileName = "unnamed",
            [FromQuery] long totalSize = 0,
            [FromQuery] int totalChunks = 0)
        {
            if (_redis == null)
                return BadRequest("Chunk approach not enabled (Redis is null).");
            if (entityId == Guid.Empty || totalSize <= 0 || totalChunks <= 0)
                return BadRequest("Invalid chunk init parameters.");

            var sessionId = Guid.NewGuid();
            var data = new ChunkSessionRedis
            {
                SessionId = sessionId,
                EntityId = entityId,
                EntityType = entityType,
                FileName = fileName,
                TotalSize = totalSize,
                TotalChunks = totalChunks,
                ReceivedChunks = new HashSet<int>(),
                IsComplete = false
            };

            var chunkFolder = GetChunkFolder(sessionId);
            Directory.CreateDirectory(chunkFolder);

            var db = _redis.GetDatabase();
            var key = GetRedisKey(sessionId);
            var json = JsonSerializer.Serialize(data);
            await db.StringSetAsync(key, json, TimeSpan.FromHours(12));

            return Ok(new ChunkInitResponse
            {
                SessionId = sessionId,
                Message = "Chunk session started."
            });
        }

        [HttpPost("chunked-upload")]
        [Authorize]
        public async Task<IActionResult> ChunkedUpload(
            [FromQuery] Guid sessionId,
            [FromQuery] int chunkIndex,
            [FromForm] IFormFile chunkFile)
        {
            if (_redis == null)
                return BadRequest("Chunk approach not enabled.");

            var db = _redis.GetDatabase();
            var key = GetRedisKey(sessionId);
            var raw = await db.StringGetAsync(key);
            if (raw.IsNullOrEmpty)
                return BadRequest("Session not found or expired.");

            var data = JsonSerializer.Deserialize<ChunkSessionRedis>(raw);
            if (data.IsComplete)
                return BadRequest("Session is already complete.");

            if (chunkFile == null || chunkFile.Length == 0)
                return BadRequest("No chunk file or it's empty.");

            var chunkFolder = GetChunkFolder(sessionId);
            if (!Directory.Exists(chunkFolder))
                return BadRequest("Chunk folder missing or expired.");

            var chunkPath = Path.Combine(chunkFolder, $"{chunkIndex:D5}.chunk");
            try
            {
                using var fs = new FileStream(chunkPath, FileMode.Create, FileAccess.Write);
                await chunkFile.CopyToAsync(fs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving chunk {Idx} for session {SessionId}", chunkIndex, sessionId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error saving chunk");
            }

            data.ReceivedChunks.Add(chunkIndex);

            var updated = JsonSerializer.Serialize(data);
            await db.StringSetAsync(key, updated, TimeSpan.FromHours(12));

            return Ok(new { Message = $"Chunk {chunkIndex} received." });
        }

        [HttpPost("chunked-complete")]
        [Authorize]
        public async Task<IActionResult> ChunkedComplete(
            [FromQuery] Guid sessionId,
            [FromQuery] string contentType = "Asset")
        {
            if (_redis == null)
                return BadRequest("Chunk approach not enabled.");

            var db = _redis.GetDatabase();
            var key = GetRedisKey(sessionId);
            var raw = await db.StringGetAsync(key);
            if (raw.IsNullOrEmpty)
                return BadRequest("Session not found or expired in Redis.");

            var data = JsonSerializer.Deserialize<ChunkSessionRedis>(raw);
            if (data.IsComplete)
                return BadRequest("Session is already complete.");

            if (data.ReceivedChunks.Count != data.TotalChunks)
                return BadRequest("Not all chunks are uploaded yet.");

            var chunkFolder = GetChunkFolder(sessionId);
            if (!Directory.Exists(chunkFolder))
                return BadRequest("Chunk folder missing.");

            // Reassemble
            var chunkFiles = Directory.GetFiles(chunkFolder, "*.chunk").OrderBy(f => f).ToList();
            if (chunkFiles.Count != data.TotalChunks)
                return BadRequest("Mismatch chunk file count vs totalChunks.");

            var assembledPath = Path.Combine(chunkFolder, "assembled.tmp");
            try
            {
                using var outStream = new FileStream(assembledPath, FileMode.Create);
                foreach (var chunkFile in chunkFiles)
                {
                    using var inStream = new FileStream(chunkFile, FileMode.Open);
                    await inStream.CopyToAsync(outStream);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assembling chunks for session {SessionId}", sessionId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error assembling chunks");
            }

            var fi = new FileInfo(assembledPath);
            if (fi.Length != data.TotalSize)
                return BadRequest("Assembled file size mismatch totalSize.");

            // Convert to IFormFile
            var finalFormFile = new PhysicalFileFormFile(assembledPath, data.FileName, fi.Length);
            if (!_contentService.ValidateFile(finalFormFile, out var err, out var _))
                return BadRequest($"Assembled file invalid: {err}");

            // Upload final to correct bucket
            var url = await _contentService.UploadFileAsync(finalFormFile, data.EntityId, "chunkuser");

            // Build DB record
            var cType = contentType.Equals("Image", StringComparison.OrdinalIgnoreCase)
                ? ContentType.Image
                : ContentType.Asset;
            var record = new Content
            {
                Id = Guid.NewGuid(),
                EntityId = data.EntityId,
                EntityType = data.EntityType,
                ContentType = cType,
                Url = url,
                BlobName = Path.GetFileName(url),
                CreatedAt = DateTime.UtcNow
            };
            await _contentRepository.AddContentsAsync(new List<Content> { record });
            await _contentRepository.SaveChangesAsync();

            data.IsComplete = true;
            await db.KeyDeleteAsync(key);

            Directory.Delete(chunkFolder, true);

            return Ok(new
            {
                record.Id,
                record.EntityId,
                record.EntityType,
                record.Url,
                record.ContentType
            });
        }

        // ============= Additional Endpoints ==============

        /// <summary>
        /// Let the user see the content items for a product
        /// </summary>
        [HttpGet("list")]
        public async Task<ActionResult<List<ContentDto>>> ListContent(Guid entityId, string entityType = "Product")
        {
            var contents = await _contentRepository.GetContentsByEntityIdAsync(entityId, entityType);
            var dtos = contents.Select(c => new ContentDto
            {
                Id = c.Id,
                EntityId = c.EntityId,
                EntityType = c.EntityType,
                Url = c.Url,
                ContentType = c.ContentType
            }).ToList();
            return Ok(dtos);
        }

        /// <summary>
        /// Only authorized users can delete content
        /// </summary>
        [HttpDelete("{contentId}")]
        [Authorize]
        public async Task<IActionResult> DeleteContent(Guid contentId)
        {
            var content = await _contentRepository.GetContentByIdAsync(contentId);
            if (content == null) return NotFound();
            _contentRepository.RemoveContent(content);
            await _contentRepository.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// If user wants to get a short-living presigned URL for a private asset
        /// We also inline-check if the user purchased the product in Orders -> OrderItems
        /// </summary>
        [HttpGet("{contentId}/presigned-url")]
        [Authorize]
        public async Task<ActionResult<string>> GetPresignedUrl(Guid contentId, int expiryInSeconds = 300)
        {
            var content = await _contentRepository.GetContentByIdAsync(contentId);
            if (content == null) return NotFound();

            // If it's an image => we can just return the direct URL (public-images)
            if (content.ContentType == ContentType.Image)
            {
                return Ok(content.Url);
            }

            // If it's an asset => check if user purchased
            if (content.ContentType == ContentType.Asset)
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                bool paid = await UserHasPurchasedAsset(userId, content.EntityId);
                if (!paid) return Forbid();

                // parse bucketName/objectName from content.Url
                // e.g. "https://minio.local.haworks.com/private-assets/username/productassets/..."
                var baseUrl = "https://minio.local.haworks.com/";
                if (!content.Url.StartsWith(baseUrl))
                    return BadRequest("URL not recognized for presigned approach.");

                var presigned = await _contentService.GetPreSignedUrlAsync(content.Url);
                return Ok(presigned);
            }

            return BadRequest("Unknown content type");
        }

        // ======= Inline Purchase Check Logic =======
        private async Task<bool> UserHasPurchasedAsset(string userId, Guid productId)
        {
            if (string.IsNullOrEmpty(userId)) return false;

            // We assume there's an Order with "UserId" and "Status=Completed"
            var hasPurchase = await _db.Orders
                .Where(o => o.UserId == userId && o.Status == OrderStatus.Completed)
                .SelectMany(o => o.OrderItems)
                .AnyAsync(oi => oi.ProductId == productId);

            return hasPurchase;
        }

        // chunk session
        private string GetRedisKey(Guid sessionId) => $"chunkSession:{sessionId}";
        private string GetChunkFolder(Guid sessionId)
        {
            var root = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "tempchunks");
            return Path.Combine(root, sessionId.ToString());
        }
    }

    // Minimal chunk classes
    public class ChunkInitResponse
    {
        public Guid SessionId { get; set; }
        public string Message { get; set; }
    }

    public class ChunkSessionRedis
    {
        public Guid SessionId { get; set; }
        public Guid EntityId { get; set; }
        public string EntityType { get; set; }
        public string FileName { get; set; }
        public long TotalSize { get; set; }
        public int TotalChunks { get; set; }
        public HashSet<int> ReceivedChunks { get; set; }
        public bool IsComplete { get; set; }
    }
}
