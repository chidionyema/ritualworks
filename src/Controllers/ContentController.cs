using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using haworks.Contracts;
using haworks.Models;
using haworks.Dto;
using haworks.Db;
using haworks.Extensions;
using Haworks.Infrastructure.Repositories;
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
            _storageService = storageService;
            _fileValidator = fileValidator;
            _chunkedService = chunkedService;
            _contentRepository = contentRepository;
            _logger = logger;
        }

        [HttpPost("upload")]
        [RequestSizeLimit(100_000_000)] // 100MB
        public async Task<IActionResult> UploadFile(
            [FromQuery] Guid entityId,
            [FromForm] IFormFile file)
        {
            var validationResult = await _fileValidator.ValidateAsync(file);
            if (!validationResult.IsValid)
            {
                return BadRequest(validationResult.Errors);
            }

            await using var fileStream = file.OpenReadStream();
            string userId = User.GetUserId() ?? "unknown";

            ContentUploadResult uploadResult = await _storageService.UploadAsync(
                fileStream,
                GetBucketForType(validationResult.FileType),
                GenerateObjectName(file.FileName, userId),
                file.ContentType,
                GetSecurityTags(validationResult.FileType)
            );

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

            await _contentRepository.AddContentsAsync(new[] { content });

            return CreatedAtAction(nameof(GetContent),
                new { id = content.Id },
                MapToDto(content));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetContent(Guid id)
        {
            Content? content = await _contentRepository.GetContentByIdAsync(id);
            if (content == null)
            {
                return NotFound();
            }
            return Ok(MapToDto(content));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteContent(Guid id)
        {
            var content = await _contentRepository.GetContentByIdAsync(id);
            if (content == null)
            {
                return NotFound();
            }

            await _contentRepository.RemoveContent(content);
            return NoContent();
        }

        [HttpPost("chunked/init")]
        [RequestSizeLimit(10_000)] // Limit the size of the initialization request
        public async Task<IActionResult> InitChunkSession([FromBody] ChunkSessionRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var session = await _chunkedService.InitSessionAsync(request);
                return CreatedAtAction(nameof(GetChunkSessionStatus), new { sessionId = session.Id }, session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing chunk session for entity {EntityId} and file {FileName}", request.EntityId, request.FileName);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to initialize chunk session.");
            }
        }


        [HttpPost("chunked/{sessionId}/{chunkIndex}")]
        [RequestSizeLimit(100_000_000)] // Adjust chunk size limit as needed
        public async Task<IActionResult> UploadChunk(Guid sessionId, int chunkIndex, IFormFile chunkFile)
        {
            if (chunkFile == null || chunkFile.Length == 0)
            {
                return BadRequest("Invalid chunk file.");
            }

            try
            {
                await _chunkedService.ProcessChunkAsync(sessionId, chunkIndex, chunkFile.OpenReadStream());
                return Ok(new { Message = $"Chunk {chunkIndex} uploaded successfully for session {sessionId}" });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidDataException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chunk {ChunkIndex} for session {SessionId}", chunkIndex, sessionId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to process chunk.");
            }
        }


        [HttpPost("chunked/complete/{sessionId}")]
        public async Task<IActionResult> CompleteChunkSession(Guid sessionId)
        {
            try
            {
                var content = await _chunkedService.CompleteSessionAsync(sessionId, User.GetUserId() ?? "unknown");
                return CreatedAtAction(nameof(GetContent), new { id = content.Id }, MapToDto(content));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message); // Session incomplete or invalid
            }
            catch (TimeoutException ex)
            {
                return BadRequest(ex.Message); // Session expired
            }
            catch (SecurityException ex)
            {
                return BadRequest(ex.Message); // Virus detected
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing chunk session {SessionId}", sessionId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to complete chunk session.");
            }
        }

        [HttpGet("chunked/session/{sessionId}")]
        public async Task<IActionResult> GetChunkSessionStatus(Guid sessionId)
        {
            try
            {
                var session = await _chunkedService.GetSessionAsync(sessionId);
                return Ok(session);
            }
            catch (InvalidOperationException)
            {
                return NotFound($"Session {sessionId} not found.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chunk session {SessionId}", sessionId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to retrieve chunk session status.");
            }
        }


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