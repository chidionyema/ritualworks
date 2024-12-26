using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

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

        public ContentService(MinioClient minioClient, ILogger<ContentService> logger)
        {
            _minioClient = minioClient ?? throw new ArgumentNullException(nameof(minioClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool ValidateFile(IFormFile file, out string validationError, out string fileType)
        {
            validationError = string.Empty;
            fileType = string.Empty;

            if (file == null || file.Length == 0)
            {
                validationError = "No file uploaded.";
                return false;
            }

            if (file.Length > _maxFileSize)
            {
                validationError = "File size exceeds the maximum allowed size.";
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
                return false;
            }
        }

        public async Task<string> UploadFileAsync(IFormFile file, Guid productId, string username)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var type = _allowedImageTypes.Contains(fileExtension) ? "productimages" : "productassets";
            var sanitizedFileName = SanitizeFileName(file.FileName);
            var objectName = $"{username}/{type}/{sanitizedFileName}";

            await EnsureBucketExistsAsync(_bucketName);

            try
            {
                _logger.LogInformation("Uploading file to MinIO: {ObjectName}", objectName);

                var putObjectArgs = new PutObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(objectName)
                    .WithStreamData(file.OpenReadStream())
                    .WithObjectSize(file.Length)
                    .WithContentType(GetContentType(sanitizedFileName));
                    

                await _minioClient.PutObjectAsync(putObjectArgs);
                
            }
            catch (MinioException ex)
            {
                _logger.LogError(ex, "Error occurred while uploading to MinIO: {Message}", ex.Message);
                throw new Exception("Failed to upload to MinIO.", ex);
            }

            var resultUrl = $"https://{_minioDomain}/{_bucketName}/{objectName}";
            _logger.LogInformation("File uploaded successfully. Accessible at: {ResultUrl}", resultUrl);
            return resultUrl;
        }

        public async Task<string> GetPreSignedUrlAsync(string filePath, int expiryInSeconds = 3600)
        {
            try
            {
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

        private string GetContentType(string fileName)
        {
            return fileName switch
            {
                string ext when ext.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || ext.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) => "image/jpeg",
                string ext when ext.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => "image/png",
                string ext when ext.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) => "image/gif",
                string ext when ext.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) => "application/pdf",
                string ext when ext.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) || ext.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) => "application/msword",
                string ext when ext.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || ext.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) => "application/zip",
                _ => "application/octet-stream"
            };
        }

        private string SanitizeFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            return fileName.Replace(" ", "_").Replace("(", "").Replace(")", "");
        }
    }
}
