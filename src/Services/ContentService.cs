using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Polly; // For optional retry logic

namespace haworks.Services
{
    public class ContentService : IContentService
    {
        private readonly MinioClient _minioClient;
        private readonly ILogger<ContentService> _logger;

        private static readonly List<string> _allowedImageTypes = new List<string> { ".jpg", ".jpeg", ".png", ".gif" };
        private static readonly List<string> _allowedAssetTypes = new List<string> { ".pdf", ".doc", ".docx", ".zip", ".rar" };

        private const long _maxFileSize = 100 * 1024 * 1024; // 100 MB
        private readonly string _bucketName = "haworks-bucket"; // Replace with your bucket name
        private readonly string _minioDomain = "minio.local.haworks.com"; // Replace with your actual domain

        // OPTIONAL: If you'd like some basic retry logic around MinIO operations
        private readonly IAsyncPolicy _uploadPolicy;

        public ContentService(MinioClient minioClient, ILogger<ContentService> logger)
        {
            _minioClient = minioClient ?? throw new ArgumentNullException(nameof(minioClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Example: Retry up to 3 times for MinioException or network I/O failures
            _uploadPolicy = Policy
                .Handle<MinioException>()
                .Or<IOException>()
                .WaitAndRetryAsync(retryCount: 3, sleepDurationProvider: attempt => TimeSpan.FromSeconds(2 * attempt),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(exception,
                            "Retry {RetryCount} for file upload. Waiting {TimeSpan} before next attempt.",
                            retryCount, timeSpan);
                    });
        }

        /// <summary>
        /// Validates an uploaded file's size and extension to determine if it is an image or asset.
        /// </summary>
        /// <returns>
        /// - <c>true</c> if valid
        /// - <c>false</c> otherwise, with a <c>validationError</c> message
        /// </returns>
       public bool ValidateFile(IFormFile file, out string error, out bool isImage)
        {
            error = string.Empty;
            isImage = false;

            if (file == null || file.Length == 0)
            {
                error = "No file uploaded or file is empty.";
                return false;
            }

            if (file.Length > _maxFileSize)
            {
                error = "File size exceeds the maximum allowed size (100 MB).";
                return false;
            }

            var fileExtension = Path.GetExtension(file.FileName)?.ToLowerInvariant();

            if (string.IsNullOrEmpty(fileExtension))
            {
                error = "File has no valid extension.";
                return false;
            }

            if (_allowedImageTypes.Contains(fileExtension))
            {
                isImage = true;
                return true;
            }

            if (_allowedAssetTypes.Contains(fileExtension))
            {
                return true;
            }

            error = $"Unsupported file type: {fileExtension}";
            return false;
        }


        /// <summary>
        /// Uploads a file to MinIO.
        /// </summary>
        /// <param name="file">The IFormFile from the request</param>
        /// <param name="productId">An identifier for the product</param>
        /// <param name="username">The user uploading this file</param>
        /// <returns>A fully qualified URL to the uploaded file</returns>
        /// <exception cref="Exception">Throws if upload fails or verification fails</exception>
        public async Task<string> UploadFileAsync(IFormFile file, Guid productId, string username)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));

            if (file.Length == 0)
            {
                _logger.LogWarning("Attempted to upload an empty file: {FileName}", file.FileName);
                throw new InvalidOperationException("File is empty. Cannot upload an empty file.");
            }

            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var type = _allowedImageTypes.Contains(fileExtension) ? "productimages" : "productassets";

            var sanitizedFileName = SanitizeFileName(file.FileName);
            var objectName = $"{username}/{type}/{sanitizedFileName}";

            _logger.LogInformation("Ensuring bucket {BucketName} exists", _bucketName);
            await EnsureBucketExistsAsync(_bucketName);

            try
            {
                // Extra debug logging with file details
                _logger.LogDebug("Starting upload. Bucket: {Bucket}, Object: {Object}, FileSize: {FileSize}, ContentType: {ContentType}",
                    _bucketName, objectName, file.Length, GetContentType(sanitizedFileName));

                // The PutObjectArgs with the opened stream
                var putObjectArgs = new PutObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(objectName)
                    .WithStreamData(file.OpenReadStream())
                    .WithObjectSize(file.Length)
                    .WithContentType(GetContentType(sanitizedFileName));

                // Use retry policy to handle transient failures
                await _uploadPolicy.ExecuteAsync(async () =>
                {
                    await _minioClient.PutObjectAsync(putObjectArgs);
                });

                _logger.LogInformation("Upload to MinIO completed for Object: {ObjectName}", objectName);

                // OPTIONAL: Post-upload verification in dev/staging
                // Comment out in production if you want to avoid extra overhead
                await VerifyObjectExistsAsync(_bucketName, objectName);

                var resultUrl = $"https://{_minioDomain}/{_bucketName}/{objectName}";
                _logger.LogInformation("File uploaded successfully. Accessible at: {ResultUrl}", resultUrl);

                return resultUrl;
            }
            catch (MinioException ex)
            {
                _logger.LogError(ex, "Error occurred while uploading to MinIO: {Message}", ex.Message);
                throw new Exception("Failed to upload to MinIO.", ex);
            }
            catch (Exception ex)
            {
                // Catch all other unexpected exceptions
                _logger.LogError(ex, "Unexpected error occurred during MinIO upload: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Generates a presigned URL to retrieve a file from MinIO without requiring explicit credentials.
        /// </summary>
        public async Task<string> GetPreSignedUrlAsync(string filePath, int expiryInSeconds = 3600)
        {
            try
            {
                _logger.LogDebug("Generating pre-signed URL for {FilePath} with expiry {Expiry}s", filePath, expiryInSeconds);

                var presignedGetObjectArgs = new PresignedGetObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(filePath)
                    .WithExpiry(expiryInSeconds);

                var preSignedUrl = await _minioClient.PresignedGetObjectAsync(presignedGetObjectArgs);
                _logger.LogInformation("Generated pre-signed URL: {PreSignedUrl}", preSignedUrl);
                return preSignedUrl;
            }
            catch (MinioException ex)
            {
                _logger.LogError(ex, "Error generating pre-signed URL: {Message}", ex.Message);
                throw new Exception("Failed to generate pre-signed URL.", ex);
            }
        }

        /// <summary>
        /// Ensures the specified bucket exists, creating it if not found.
        /// </summary>
        private async Task EnsureBucketExistsAsync(string bucketName)
        {
            try
            {
                bool found = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName));
                if (!found)
                {
                    _logger.LogInformation("Bucket does not exist. Creating: {BucketName}", bucketName);
                    await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
                }
                else
                {
                    _logger.LogInformation("Bucket already exists: {BucketName}", bucketName);
                }
            }
            catch (MinioException ex)
            {
                _logger.LogError(ex, "Error ensuring bucket exists: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Verifies that a recently uploaded object actually exists in MinIO via StatObjectAsync.
        /// This is helpful to catch silent or partial failures in dev/staging.
        /// </summary>
        private async Task VerifyObjectExistsAsync(string bucketName, string objectName)
        {
            try
            {
                _logger.LogDebug("Verifying object: Bucket={Bucket}, ObjectName={ObjectName}", bucketName, objectName);

                // If object not found, this call will throw MinioException
                var stat = await _minioClient.StatObjectAsync(
                    new StatObjectArgs().WithBucket(bucketName).WithObject(objectName));

                _logger.LogDebug("Object found in MinIO. Size: {Size}, LastModified: {LastModified}",
                    stat.Size, stat.LastModified);
            }
            catch (MinioException ex)
            {
                _logger.LogError(ex, "Post-upload verification failed for {ObjectName}. Error: {Message}", objectName, ex.Message);
                throw new Exception($"Object not found or not accessible in MinIO after upload: {objectName}", ex);
            }
        }

        /// <summary>
        /// Returns a MIME type based on file extension.
        /// </summary>
        private string GetContentType(string fileName)
        {
            if (fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return "image/jpeg";
            }
            if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return "image/png";
            }
            if (fileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                return "image/gif";
            }
            if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return "application/pdf";
            }
            if (fileName.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                return "application/msword";
            }
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
            {
                return "application/zip";
            }
            return "application/octet-stream";
        }

        /// <summary>
        /// Sanitizes a file name by removing invalid characters and certain punctuation.
        /// </summary>
        private string SanitizeFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            // Replace spaces and remove parentheses
            fileName = fileName.Replace(" ", "_").Replace("(", "").Replace(")", "");
            return fileName;
        }
    }
}
