using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RitualWorks.Contracts;
using RitualWorks.Db;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RitualWorks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AssetsController : ControllerBase
    {
        private readonly IFileStorageService _fileStorageService;
        private readonly IProductRepository _productRepository;
        private readonly ILogger<AssetsController> _logger;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
        private static readonly List<string> _allowedImageTypes = new List<string> { ".jpg", ".jpeg", ".png", ".gif" };
        private static readonly List<string> _allowedAssetTypes = new List<string> { ".pdf", ".doc", ".docx", ".zip", ".rar" };
        private const long _maxFileSize = 100 * 1024 * 1024; // 100 MB
        private readonly string _rootPath = "/Users/chidionyema/Documents/code/RitualWorks/RitualWorks/";

        public AssetsController(IFileStorageService fileStorageService, IProductRepository productRepository, ILogger<AssetsController> logger)
        {
            _fileStorageService = fileStorageService;
            _productRepository = productRepository;
            _logger = logger;

            // Ensure the root path exists
            if (!Directory.Exists(_rootPath))
            {
                _logger.LogInformation($"Creating root path: {_rootPath}");
                Directory.CreateDirectory(_rootPath);
            }
        }

        [HttpPost("upload-chunk")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadFileChunk([FromForm] FileChunkUploadDto uploadDto)
        {
            _logger.LogInformation("Received request to upload file chunk: {FileName} for ProductId: {ProductId}, ChunkIndex: {ChunkIndex}, TotalChunks: {TotalChunks}", 
                uploadDto.FileName, uploadDto.ProductId, uploadDto.ChunkIndex, uploadDto.TotalChunks);

            if (!ValidateFile(uploadDto.File, out string validationError, out string fileType))
            {
                _logger.LogWarning("File validation failed: {ValidationError}", validationError);
                return BadRequest(validationError);
            }

            string tempFolder = Path.Combine(_rootPath, uploadDto.ProductId.ToString());
            string tempFilePath = Path.Combine(tempFolder, uploadDto.FileName);

            try
            {
                // Ensure the temporary folder exists
                _logger.LogInformation("Ensuring directory exists: {TempFolder}", tempFolder);
                Directory.CreateDirectory(tempFolder);

                var fileLock = _fileLocks.GetOrAdd(uploadDto.FileName, _ => new SemaphoreSlim(1, 1));
                await fileLock.WaitAsync();

                _logger.LogInformation("Writing chunk to file: {TempFilePath}, ChunkIndex: {ChunkIndex}", tempFilePath, uploadDto.ChunkIndex);

                using (var stream = new FileStream(tempFilePath, uploadDto.ChunkIndex == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None))
                {
                    await uploadDto.File.CopyToAsync(stream);
                }

                if (uploadDto.ChunkIndex == uploadDto.TotalChunks - 1)
                {
                    _logger.LogInformation("Finalizing upload for file: {TempFilePath}", tempFilePath);
                    var result = await FinalizeUpload(tempFilePath, uploadDto.FileName, fileType, uploadDto.ProductId, uploadDto.Username);
                    Directory.Delete(tempFolder, true);
                    _logger.LogInformation("Upload finalized successfully: {Result}", result);

                    return Ok(new { FileName = result });
                }

                _logger.LogInformation("Chunk uploaded successfully: {ChunkIndex}/{TotalChunks} for file {FileName}", 
                    uploadDto.ChunkIndex + 1, uploadDto.TotalChunks, uploadDto.FileName);
                return Ok(new { Message = "Chunk uploaded successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while uploading file chunk: {Message}", ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new { Error = "An error occurred while uploading the file chunk.", Details = ex.Message });
            }
            finally
            {
                fileLock.Release();
                if (uploadDto.ChunkIndex == uploadDto.TotalChunks - 1)
                {
                    _fileLocks.TryRemove(uploadDto.FileName, out _);
                }
            }
        }

        [HttpPost("upload")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadFile([FromForm] FileUploadDto uploadDto)
        {
            _logger.LogInformation("Received request to upload file: {FileName} for ProductId: {ProductId}", uploadDto.File?.FileName, uploadDto.ProductId);

            if (!ValidateFile(uploadDto.File, out string validationError, out string fileType))
            {
                _logger.LogWarning("File validation failed: {ValidationError}", validationError);
                return BadRequest(validationError);
            }

            var tempFilePath = Path.Combine(_rootPath, uploadDto.File.FileName);
            var fileLock = _fileLocks.GetOrAdd(uploadDto.File.FileName, _ => new SemaphoreSlim(1, 1));

            try
            {
                await fileLock.WaitAsync();

                _logger.LogInformation("Writing file to temp path: {TempFilePath}", tempFilePath);
                using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                {
                    await uploadDto.File.CopyToAsync(stream);
                }

                _logger.LogInformation("Finalizing upload for file: {TempFilePath}", tempFilePath);
                var result = await FinalizeUpload(tempFilePath, uploadDto.File.FileName, fileType, uploadDto.ProductId, uploadDto.Username);
                _logger.LogInformation("Upload finalized successfully: {Result}", result);

                return Ok(new { FileName = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while uploading file: {Message}", ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new { Error = "An error occurred while uploading the file.", Details = ex.Message });
            }
            finally
            {
                fileLock.Release();
                _fileLocks.TryRemove(uploadDto.File.FileName, out _);
            }
        }

        private bool ValidateFile(IFormFile file, out string validationError, out string fileType)
        {
            validationError = string.Empty;
            fileType = string.Empty;

            if (file == null || file.Length == 0)
            {
                validationError = "No file uploaded.";
                _logger.LogWarning(validationError);
                return false;
            }

            if (file.Length > _maxFileSize)
            {
                validationError = "File size exceeds the maximum allowed size.";
                _logger.LogWarning(validationError);
                return false;
            }

            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (_allowedImageTypes.Contains(fileExtension))
            {
                fileType = "products/images";
                return true;
            }
            else if (_allowedAssetTypes.Contains(fileExtension))
            {
                fileType = "products/assets";
                return true;
            }
            else
            {
                validationError = "Invalid file type.";
                _logger.LogWarning(validationError);
                return false;
            }
        }

        private async Task<string> FinalizeUpload(string tempFilePath, string fileName, string type, Guid productId, string username)
        {
            _logger.LogInformation("Starting finalization of upload for file: {TempFilePath}", tempFilePath);
            using var stream = new FileStream(tempFilePath, FileMode.Open);

            var filePath = $"{username}/{type}/{fileName}";
            _logger.LogInformation("Uploading file to storage: {FilePath}", filePath);

            var result = await _fileStorageService.UploadFileAsync(stream, filePath, append: false);
            _logger.LogInformation("File uploaded to storage with result: {Result}", result);

            try
            {
                if (type.Contains("images"))
                {
                    _logger.LogInformation("Adding product image record to database for ProductId: {ProductId}", productId);
                    var productImage = new ProductImage(Guid.NewGuid(), productId, null)
                    {
                        Url = result,
                        BlobName = fileName
                    };
                    await _productRepository.AddProductImageAsync(productImage);
                }
                else
                {
                    _logger.LogInformation("Adding product asset record to database for ProductId: {ProductId}", productId);
                    var productAsset = new ProductAsset(Guid.NewGuid(), productId, null)
                    {
                        AssetUrl = result,
                        BlobName = fileName
                    };
                    await _productRepository.AddProductAssetAsync(productAsset);
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Error occurred while saving product record to database: {Message}", dbEx.Message);
                throw; // Rethrow to be caught by the calling method
            }

            _logger.LogInformation("Deleting temporary file: {TempFilePath}", tempFilePath);
            System.IO.File.Delete(tempFilePath);
            return result;
        }
    }

    // DTOs for File Upload
    public class FileUploadDto
    {
        [Required]
        public IFormFile? File { get; set; }

        [Required]
        public Guid ProductId { get; set; }

        [Required]
        public string? Username { get; set; }
    }

    public class FileChunkUploadDto
    {
        [Required]
        public IFormFile? File { get; set; }

        [Required]
        public int ChunkIndex { get; set; }

        [Required]
        public int TotalChunks { get; set; }

        [Required]
        public string? FileName { get; set; }

        [Required]
        public Guid ProductId { get; set; }

        [Required]
        public string? Username { get; set; }
    }
}
