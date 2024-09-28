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
            _fileStorageService = fileStorageService ?? throw new ArgumentNullException(nameof(fileStorageService));
            _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

            // Check if the file is null
            if (uploadDto.File == null)
            {
                _logger.LogWarning("File upload failed: No file provided.");
                return BadRequest("No file uploaded.");
            }

            if (!ValidateFile(uploadDto.File, out string validationError, out string fileType))
            {
                _logger.LogWarning("File validation failed: {ValidationError}", validationError);
                return BadRequest(validationError);
            }

            string tempFolder = Path.Combine(_rootPath, uploadDto.ProductId.ToString());
            string tempFilePath = Path.Combine(tempFolder, uploadDto.FileName);

            SemaphoreSlim fileLock = _fileLocks.GetOrAdd(uploadDto.FileName, _ => new SemaphoreSlim(1, 1));

            try
            {
                // Ensure the temporary folder exists
                _logger.LogInformation("Ensuring directory exists: {TempFolder}", tempFolder);
                Directory.CreateDirectory(tempFolder);

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

            // Check if the file is null
            if (uploadDto.File == null)
            {
                _logger.LogWarning("File upload failed: No file provided.");
                return BadRequest("No file uploaded.");
            }

            if (!ValidateFile(uploadDto.File, out string validationError, out string fileType))
            {
                _logger.LogWarning("File validation failed: {ValidationError}", validationError);
                return BadRequest(validationError);
            }

            var tempFilePath = Path.Combine(_rootPath, uploadDto.File.FileName);
            SemaphoreSlim fileLock = _fileLocks.GetOrAdd(uploadDto.File.FileName, _ => new SemaphoreSlim(1, 1));

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
                fileType = "productimages";
                return true;
            }
            else if (_allowedAssetTypes.Contains(fileExtension))
            {
                fileType = "productassets";
                return true;
            }
            else
            {
                validationError = "Invalid file type.";
                _logger.LogWarning(validationError);
                return false;
            }
        }

        private async Task<string> FinalizeUpload(string tempFilePath, string fileName, string type, Guid productId, string? username)
        {
            _logger.LogInformation("Starting finalization of upload for file: {TempFilePath}", tempFilePath);

            // Validate inputs thoroughly
            if (string.IsNullOrWhiteSpace(tempFilePath))
            {
                _logger.LogError("Temp file path is null or empty.");
                throw new ArgumentNullException(nameof(tempFilePath), "Temp file path cannot be null or empty.");
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                _logger.LogError("File name is null or empty.");
                throw new ArgumentNullException(nameof(fileName), "File name cannot be null or empty.");
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                _logger.LogError("Type is null or empty.");
                throw new ArgumentNullException(nameof(type), "Type cannot be null or empty.");
            }

            if (productId == Guid.Empty)
            {
                _logger.LogError("Product ID is empty.");
                throw new ArgumentException("Product ID cannot be empty.", nameof(productId));
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                _logger.LogError("Username is null or empty.");
                throw new ArgumentNullException(nameof(username), "Username cannot be null or empty.");
            }

            // Log file information before proceeding
            if (!System.IO.File.Exists(tempFilePath))
            {
                _logger.LogError("Temporary file does not exist at path: {TempFilePath}", tempFilePath);
                throw new FileNotFoundException("Temporary file not found.", tempFilePath);
            }

            try
            {
                using var stream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read);
                _logger.LogInformation("Opened temporary file: {TempFilePath} for reading.", tempFilePath);

                var filePath = $"{username}/{type}/{fileName}";
                _logger.LogInformation("Uploading file to storage: {FilePath}", filePath);

                // Upload file to storage service
                var result = await _fileStorageService.UploadFileAsync(stream, filePath, append: false);
                _logger.LogInformation("File uploaded to storage successfully with result: {Result}", result);

                // Handle the database update according to the type of file (image or asset)
                if (type.Contains("images", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Adding product image record to database for ProductId: {ProductId}", productId);
                    var productImage = new ProductImage(Guid.NewGuid(), productId, null)
                    {
                        Url = result,
                        BlobName = fileName
                    };

                    if (_productRepository == null)
                    {
                        _logger.LogError("_productRepository is null. Cannot save product image.");
                        throw new InvalidOperationException("_productRepository is not initialized.");
                    }

                    await _productRepository.AddProductImageAsync(productImage);
                    _logger.LogInformation("Product image record added successfully for ProductId: {ProductId}", productId);
                }
                else
                {
                    _logger.LogInformation("Adding product asset record to database for ProductId: {ProductId}", productId);
                    var productAsset = new ProductAsset(Guid.NewGuid(), productId, null)
                    {
                        AssetUrl = result,
                        BlobName = fileName
                    };

                    if (_productRepository == null)
                    {
                        _logger.LogError("_productRepository is null. Cannot save product asset.");
                        throw new InvalidOperationException("_productRepository is not initialized.");
                    }

                    await _productRepository.AddProductAssetAsync(productAsset);
                    _logger.LogInformation("Product asset record added successfully for ProductId: {ProductId}", productId);
                }

                // Ensure the temporary file is deleted to free up space
                _logger.LogInformation("Deleting temporary file: {TempFilePath}", tempFilePath);
                System.IO.File.Delete(tempFilePath);

                if (System.IO.File.Exists(tempFilePath))
                {
                    _logger.LogWarning("Temporary file was not deleted: {TempFilePath}", tempFilePath);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while finalizing the upload: {Message}", ex.Message);
                throw; // Rethrow the exception to propagate it up the call stack
            }
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
