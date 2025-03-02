using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

// ASP.NET Core Imports
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

// Project-specific Imports
using haworks.Contracts;
using haworks.Models;
using haworks.Dto;
using haworks.Db;
using haworks.Extensions;
using Haworks.Infrastructure.Repositories;

// Additional System Imports
using System.Security;

namespace haworks.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize(Policy = "ContentUploader")]
    public class ContentController : ControllerBase
    {
        private readonly IContentStorageService _storageService;
        private readonly IFileValidator _fileValidator;
        private readonly IChunkedUploadService _chunkedService;
        private readonly IContentContextRepository _contentRepository;
        private readonly ILogger<ContentController> _logger;

        public ContentController(
            IContentStorageService storageService,
            IFileValidator fileValidator,
            IChunkedUploadService chunkedService,
            IContentContextRepository contentRepository,
            ILogger<ContentController> logger)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _fileValidator = fileValidator ?? throw new ArgumentNullException(nameof(fileValidator));
            _chunkedService = chunkedService ?? throw new ArgumentNullException(nameof(chunkedService));
            _contentRepository = contentRepository ?? throw new ArgumentNullException(nameof(contentRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost("upload")]
        [RequestSizeLimit(100_000_000)] // 100MB
        public async Task<IActionResult> UploadFile(
            [FromQuery] Guid entityId,
            [FromForm] IFormFile file)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try 
            {
                _logger.LogInformation(
                    "Starting file upload. EntityId: {EntityId}, FileName: {FileName}, ContentType: {ContentType}", 
                    entityId, file.FileName, file.ContentType);

                var validationResult = await _fileValidator.ValidateAsync(file);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning(
                        "File validation failed. Errors: {ValidationErrors}", 
                        string.Join(", ", validationResult.Errors));
                    return BadRequest(validationResult.Errors);
                }

                await using var fileStream = file.OpenReadStream();
                string userId = User.GetUserId() ?? "unknown";

                ContentUploadResult uploadResult;
                try 
                {
                    uploadResult = await _storageService.UploadAsync(
                        fileStream,
                        GetBucketForType(validationResult.FileType),
                        GenerateObjectName(file.FileName, userId),
                        file.ContentType,
                        GetSecurityTags(validationResult.FileType)
                    );
                    
                    _logger.LogInformation(
                        "File uploaded successfully. Bucket: {Bucket}, ObjectName: {ObjectName}", 
                        uploadResult.BucketName, uploadResult.ObjectName);
                }
                catch (Exception storageEx)
                {
                    _logger.LogError(storageEx, 
                        "Storage upload failed. Bucket: {Bucket}, FileName: {FileName}", 
                        GetBucketForType(validationResult.FileType), file.FileName);
                    return StatusCode(StatusCodes.Status500InternalServerError, 
                        "Failed to upload file to storage.");
                }

                ContentType parsedContentType = ParseContentType(file.ContentType);

                var content = new Content
                {
                    Id = Guid.NewGuid(),
                    EntityId = entityId,
                    EntityType = GetBucketForType(validationResult.FileType),
                    FileName = file.FileName,
                    ContentType = parsedContentType,
                    BucketName = uploadResult.BucketName,
                    ObjectName = uploadResult.ObjectName,
                    ETag = uploadResult.VersionId,
                    FileSize = fileStream.Length,
                    StorageDetails = uploadResult.StorageDetails,
                    Path = uploadResult.Path
                };

                try 
                {
                    await _contentRepository.AddContentsAsync(new[] { content });
                    _logger.LogInformation(
                        "Content record added. ContentId: {ContentId}, Duration: {Duration}ms", 
                        content.Id, stopwatch.ElapsedMilliseconds);
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, 
                        "Database insertion failed. ContentId: {ContentId}", 
                        content.Id);
                    return StatusCode(StatusCodes.Status500InternalServerError, 
                        "Failed to save content to database.");
                }

                return CreatedAtAction(nameof(GetContent),
                    new { id = content.Id },
                    MapToDto(content));
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, 
                    "Unexpected error during file upload. EntityId: {EntityId}, Duration: {Duration}ms", 
                    entityId, stopwatch.ElapsedMilliseconds);
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    "An unexpected error occurred during file upload.");
            }
            finally 
            {
                stopwatch.Stop();
            }
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetContent(Guid id)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try 
            {
                _logger.LogInformation("Retrieving content. ContentId: {ContentId}", id);

                Content? content = await _contentRepository.GetContentByIdAsync(id);
                
                if (content == null)
                {
                    _logger.LogWarning(
                        "Content not found. ContentId: {ContentId}, Duration: {Duration}ms", 
                        id, stopwatch.ElapsedMilliseconds);
                    return NotFound();
                }

                _logger.LogInformation(
                    "Content retrieved successfully. ContentId: {ContentId}, Duration: {Duration}ms", 
                    id, stopwatch.ElapsedMilliseconds);
                
                return Ok(MapToDto(content));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error retrieving content. ContentId: {ContentId}, Duration: {Duration}ms", 
                    id, stopwatch.ElapsedMilliseconds);
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    "An error occurred while retrieving the content.");
            }
            finally 
            {
                stopwatch.Stop();
            }
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteContent(Guid id)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try 
            {
                _logger.LogInformation("Attempting to delete content. ContentId: {ContentId}", id);

                var content = await _contentRepository.GetContentByIdAsync(id);
                
                if (content == null)
                {
                    _logger.LogWarning(
                        "Content not found for deletion. ContentId: {ContentId}, Duration: {Duration}ms", 
                        id, stopwatch.ElapsedMilliseconds);
                    return NotFound();
                }

                await _contentRepository.RemoveContent(content);
                
                _logger.LogInformation(
                    "Content deleted successfully. ContentId: {ContentId}, Duration: {Duration}ms", 
                    id, stopwatch.ElapsedMilliseconds);
                
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error deleting content. ContentId: {ContentId}, Duration: {Duration}ms", 
                    id, stopwatch.ElapsedMilliseconds);
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    "An error occurred while deleting the content.");
            }
            finally 
            {
                stopwatch.Stop();
            }
        }

        [HttpPost("chunked/init")]
        [RequestSizeLimit(10_000)]
        public async Task<IActionResult> InitChunkSession([FromBody] ChunkSessionRequest request)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try 
            {
                _logger.LogInformation(
                    "Initializing chunk session. EntityId: {EntityId}, FileName: {FileName}", 
                    request.EntityId, request.FileName);

                if (!ModelState.IsValid)
                {
                    _logger.LogWarning(
                        "Invalid chunk session request. Errors: {ModelErrors}", 
                        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                    return BadRequest(ModelState);
                }

                var session = await _chunkedService.InitSessionAsync(request);
                
                _logger.LogInformation(
                    "Chunk session initialized. SessionId: {SessionId}, Duration: {Duration}ms", 
                    session.Id, stopwatch.ElapsedMilliseconds);
                
                return CreatedAtAction(
                    nameof(GetChunkSessionStatus), 
                    new { sessionId = session.Id }, 
                    session
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error initializing chunk session. EntityId: {EntityId}, Duration: {Duration}ms", 
                    request.EntityId, stopwatch.ElapsedMilliseconds);
                
                return StatusCode(
                    StatusCodes.Status500InternalServerError, 
                    "Failed to initialize chunk session."
                );
            }
            finally 
            {
                stopwatch.Stop();
            }
        }

        [HttpPost("chunked/{sessionId}/{chunkIndex}")]
        [RequestSizeLimit(100_000_000)]
        public async Task<IActionResult> UploadChunk(
            Guid sessionId, 
            int chunkIndex, 
            IFormFile chunkFile)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try 
            {
                _logger.LogInformation(
                    "Uploading chunk. SessionId: {SessionId}, ChunkIndex: {ChunkIndex}", 
                    sessionId, chunkIndex);

                if (chunkFile == null || chunkFile.Length == 0)
                    return BadRequest("Invalid chunk file");

                // Read entire chunk into memory
                await using var stream = chunkFile.OpenReadStream();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                byte[] chunkData = memoryStream.ToArray();
                if (stream.CanSeek)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                }

                await _chunkedService.ProcessChunkAsync(
                    sessionId, 
                    chunkIndex, 
                    new MemoryStream(chunkData) // Fresh stream with full data
                );

                _logger.LogInformation(
                    "Chunk uploaded. Size: {Bytes} bytes", 
                    chunkData.Length);
                
                return Ok(new { Message = $"Chunk {chunkIndex} uploaded" });
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogWarning(ex, "Invalid chunk index");
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation during upload");
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chunk processing error");
                return StatusCode(500, "Failed to process chunk");
            }
            finally 
            {
                stopwatch.Stop();
            }
        }

        [HttpPost("chunked/complete/{sessionId}")]
        public async Task<IActionResult> CompleteChunkSession(Guid sessionId)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try 
            {
                _logger.LogInformation(
                    "Completing chunk session. SessionId: {SessionId}", 
                    sessionId);

                var content = await _chunkedService.CompleteSessionAsync(
                    sessionId, 
                    User.GetUserId() ?? "unknown"
                );
                
                _logger.LogInformation(
                    "Chunk session completed. SessionId: {SessionId}, ContentId: {ContentId}, Duration: {Duration}ms", 
                    sessionId, content.Id, stopwatch.ElapsedMilliseconds);
                
                return CreatedAtAction(
                    nameof(GetContent), 
                    new { id = content.Id }, 
                    MapToDto(content)
                );
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, 
                    "Invalid operation completing chunk session. SessionId: {SessionId}", 
                    sessionId);
                return BadRequest(ex.Message);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, 
                    "Chunk session timeout. SessionId: {SessionId}", 
                    sessionId);
                return BadRequest(ex.Message);
            }
            catch (SecurityException ex)
            {
                _logger.LogWarning(ex, 
                    "Security exception in chunk session. SessionId: {SessionId}", 
                    sessionId);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error completing chunk session. SessionId: {SessionId}, Duration: {Duration}ms", 
                    sessionId, stopwatch.ElapsedMilliseconds);
                
                return StatusCode(
                    StatusCodes.Status500InternalServerError, 
                    "Failed to complete chunk session."
                );
            }
            finally 
            {
                stopwatch.Stop();
            }
        }

        [HttpGet("chunked/session/{sessionId}")]
        public async Task<IActionResult> GetChunkSessionStatus(Guid sessionId)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try 
            {
                _logger.LogInformation(
                    "Retrieving chunk session status. SessionId: {SessionId}", 
                    sessionId);

                var session = await _chunkedService.GetSessionAsync(sessionId);
                
                _logger.LogInformation(
                    "Chunk session status retrieved. SessionId: {SessionId}, Duration: {Duration}ms", 
                    sessionId, stopwatch.ElapsedMilliseconds);
                
                return Ok(session);
            }
            catch (InvalidOperationException)
            {
                _logger.LogWarning(
                    "Chunk session not found. SessionId: {SessionId}, Duration: {Duration}ms", 
                    sessionId, stopwatch.ElapsedMilliseconds);
                
                return NotFound($"Session {sessionId} not found.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error retrieving chunk session status. SessionId: {SessionId}, Duration: {Duration}ms", 
                    sessionId, stopwatch.ElapsedMilliseconds);
                
                return StatusCode(
                    StatusCodes.Status500InternalServerError, 
                    "Failed to retrieve chunk session status."
                );
            }
            finally 
            {
                stopwatch.Stop();
            }
        }

        // --- Private Helper Methods ---

        private string GetBucketForType(string fileType) =>
            fileType switch
            {
                "Image" => "images",
                "Document" => "documents",
                _ => "other"
            };

        private Dictionary<string, string> GetSecurityTags(string fileType) =>
            new Dictionary<string, string>
            {
                ["FileType"] = fileType,
                ["UploadedBy"] = User.GetUserId() ?? "unknown"
            };

        private string GenerateObjectName(string fileName, string userId) =>
            $"{userId}/{Guid.NewGuid()}{Path.GetExtension(fileName)}";

        private ContentDto MapToDto(Content content) =>
            new ContentDto(
                content.Id,
                content.EntityId,
                content.EntityType,
                content.Path,
                content.ContentType.ToString(),
                content.FileSize);

        private ContentType ParseContentType(string mime) =>
            mime.ToLower() switch
            {
                "image/jpeg" or "image/jpg" or "image/png" or "image/gif" => ContentType.Image,
                "application/pdf" => ContentType.Document,
                "video/mp4" or "video/avi" or "video/mpeg" => ContentType.Video,
                _ => ContentType.Other
            };
    }
}
