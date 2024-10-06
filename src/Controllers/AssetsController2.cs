using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using RitualWorks.Contracts;
using RitualWorks.Db;

namespace RitualWorks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class Assets2Controller : ControllerBase
    {
        private readonly MinioClient _minioClient;
        private readonly IProductRepository _productRepository;
        private readonly ILogger<AssetsController> _logger;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
        private static readonly List<string> _allowedImageTypes = new List<string> { ".jpg", ".jpeg", ".png", ".gif" };
        private static readonly List<string> _allowedAssetTypes = new List<string> { ".pdf", ".doc", ".docx", ".zip", ".rar" };
        private const long _maxFileSize = 100 * 1024 * 1024; // 100 MB
        private readonly string _bucketName = "ritualworks-bucket"; // Replace with your bucket name

        public Assets2Controller(MinioClient minioClient, IProductRepository productRepository, ILogger<AssetsController> logger)
        {
            _minioClient = minioClient ?? throw new ArgumentNullException(nameof(minioClient));
            _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Ensure the bucket exists
            InitializeBucketAsync().GetAwaiter().GetResult();
        }
private async Task InitializeBucketAsync()
{
    // Check if the bucket exists using BucketExistsArgs
    var bucketExistsArgs = new Minio.DataModel.Args.BucketExistsArgs().WithBucket(_bucketName);
    bool bucketExists = await _minioClient.BucketExistsAsync(bucketExistsArgs);
    
    if (!bucketExists)
    {
        // Create the bucket using MakeBucketArgs
        var makeBucketArgs = new Minio.DataModel.Args.MakeBucketArgs().WithBucket(_bucketName);
        await _minioClient.MakeBucketAsync(makeBucketArgs);
        _logger.LogInformation($"Bucket '{_bucketName}' created successfully.");
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

            string tempFilePath = Path.Combine(Path.GetTempPath(), uploadDto.FileName);
            SemaphoreSlim fileLock = _fileLocks.GetOrAdd(uploadDto.FileName, _ => new SemaphoreSlim(1, 1));

            try
            {
                await fileLock.WaitAsync();

                _logger.LogInformation("Writing chunk to file: {TempFilePath}, ChunkIndex: {ChunkIndex}", tempFilePath, uploadDto.ChunkIndex);

                using (var stream = new FileStream(tempFilePath, uploadDto.ChunkIndex == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None))
                {
                    await uploadDto.File.CopyToAsync(stream);
                }

                if (uploadDto.ChunkIndex == uploadDto.TotalChunks - 1)
                {
                    _logger.LogInformation("Finalizing upload for file: {TempFilePath}", tempFilePath);
                    var result = await FinalizeUploadAsync(tempFilePath, uploadDto.FileName, fileType, uploadDto.ProductId, uploadDto.Username);
                    System.IO.File.Delete(tempFilePath);
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
        public async Task<IActionResult> UploadFile([FromForm] FileUploadDto2 uploadDto)
        {
            _logger.LogInformation("Received request to upload file: {FileName} for ProductId: {ProductId}", uploadDto.File?.FileName, uploadDto.ProductId);

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

            var tempFilePath = Path.Combine(Path.GetTempPath(), uploadDto.File.FileName);
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
                var result = await FinalizeUploadAsync(tempFilePath, uploadDto.File.FileName, fileType, uploadDto.ProductId, uploadDto.Username);
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

        private async Task<string> FinalizeUploadAsync(string tempFilePath, string fileName, string type, Guid productId, string? username)
        {
            _logger.LogInformation("Starting finalization of upload for file: {TempFilePath}", tempFilePath);

            // Upload file to MinIO
         var objectName = $"{username}/{type}/{fileName}";
            await using (var stream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
            {
                try
                {
                    _logger.LogInformation("Uploading file to MinIO: {ObjectName}", objectName);

                    // Create PutObjectArgs with the required parameters
                    var putObjectArgs = new PutObjectArgs()
                        .WithBucket(_bucketName)         // Specify the bucket name
                        .WithObject(objectName)          // Specify the object name
                        .WithStreamData(stream)          // Set the stream data
                        .WithObjectSize(stream.Length)   // Specify the length of the stream
                        .WithContentType("application/octet-stream"); // Set content type

                    await _minioClient.PutObjectAsync(putObjectArgs);
                    
                    _logger.LogInformation("File uploaded to MinIO successfully: {ObjectName}", objectName);
                }
                catch (MinioException ex)
                {
                    _logger.LogError(ex, "Error occurred while uploading to MinIO: {Message}", ex.Message);
                    throw new Exception("Failed to upload to MinIO.", ex);
                }
            }
            // Save file details to the database (like ProductImage or ProductAsset)
            var resultUrl = $"https://{_bucketName}/{objectName}"; // This is just an example URL, adjust as needed

            if (type.Contains("images", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Adding product image record to database for ProductId: {ProductId}", productId);
                var productImage = new ProductImage(Guid.NewGuid(), productId, null)
                {
                    Url = resultUrl,
                    BlobName = fileName
                };
                await _productRepository.AddProductImageAsync(productImage);
                _logger.LogInformation("Product image record added successfully for ProductId: {ProductId}", productId);
            }
            else
            {
                _logger.LogInformation("Adding product asset record to database for ProductId: {ProductId}", productId);
                var productAsset = new ProductAsset(Guid.NewGuid(), productId, null)
                {
                    AssetUrl = resultUrl,
                    BlobName = fileName
                };
                await _productRepository.AddProductAssetAsync(productAsset);
                _logger.LogInformation("Product asset record added successfully for ProductId: {ProductId}", productId);
            }

            // Delete the temporary file after upload
            _logger.LogInformation("Deleting temporary file: {TempFilePath}", tempFilePath);
            System.IO.File.Delete(tempFilePath);

            return resultUrl;
        }
    }

    // DTOs for File Upload
    public class FileUploadDto2
    {
        [Required]
        public IFormFile? File { get; set; }

        [Required]
        public Guid ProductId { get; set; }

        [Required]
        public string? Username { get; set; }
    }

    public class FileChunkUploadDto2
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
