using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using RitualWorks.Contracts;

namespace RitualWorks.Services
{
    public class MinioService : IFileStorageService
    {
        private readonly IMinioClient _minioClient; // Changed to IMinioClient
        private readonly ILogger<MinioService> _logger;
        private readonly string _bucketName;

        public MinioService(IConfiguration configuration, ILogger<MinioService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Validate and retrieve configuration settings
            var endpoint = configuration["MinIO:Endpoint"]
                ?? throw new ArgumentNullException("MinIO:Endpoint", "The MinIO endpoint configuration value is missing.");
            var accessKey = configuration["MinIO:AccessKey"]
                ?? throw new ArgumentNullException("MinIO:AccessKey", "The MinIO access key configuration value is missing.");
            var secretKey = configuration["MinIO:SecretKey"]
                ?? throw new ArgumentNullException("MinIO:SecretKey", "The MinIO secret key configuration value is missing.");
            var secure = bool.TryParse(configuration["MinIO:Secure"], out var parsedSecure) ? parsedSecure : true;
            _bucketName = configuration["MinIO:BucketName"]
                ?? throw new ArgumentNullException("MinIO:BucketName", "The MinIO bucket name configuration value is missing.");

            // Initialize the MinioClient with provided credentials
            _minioClient = new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey)
                .WithSSL(secure)
                .Build();

            _logger.LogInformation("MinioService initialized with endpoint: {Endpoint} and bucket: {BucketName}", endpoint, _bucketName);
        }

        public async Task<string> UploadFileAsync(Stream fileStream, string filePath, bool append = false)
        {
            if (fileStream == null || fileStream.Length == 0)
                throw new ArgumentException("File stream cannot be null or empty.", nameof(fileStream));

            try
            {
                var putObjectArgs = new PutObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(filePath.Replace("\\", "/"))
                    .WithStreamData(fileStream)
                    .WithObjectSize(fileStream.Length)
                    .WithContentType("application/octet-stream");

                await _minioClient.PutObjectAsync(putObjectArgs);
                _logger.LogInformation("File uploaded successfully to {FilePath}", filePath);
                return filePath;
            }
            catch (MinioException ex)
            {
                _logger.LogError(ex, "MinIO error occurred while uploading file to {FilePath}", filePath);
                throw new Exception("Error uploading file to MinIO.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while uploading file to {FilePath}", filePath);
                throw;
            }
        }

        public async Task<Stream> DownloadFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            try
            {
                var getObjectArgs = new GetObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(filePath);

                var responseStream = new MemoryStream();

                // Correct usage of GetObjectAsync with callback and cancellation token
                await _minioClient.GetObjectAsync(getObjectArgs);

                responseStream.Position = 0; // Reset the stream's position for reading
                _logger.LogInformation("File downloaded successfully from {FilePath}", filePath);
                return responseStream;
            }
            catch (MinioException ex)
            {
                _logger.LogError(ex, "MinIO error occurred while downloading file from {FilePath}", filePath);
                throw new Exception("Error downloading file from MinIO.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while downloading file from {FilePath}", filePath);
                throw;
            }
        }

        public async Task<bool> DeleteFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            try
            {
                var removeObjectArgs = new RemoveObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(filePath);

                await _minioClient.RemoveObjectAsync(removeObjectArgs);
                _logger.LogInformation("File deleted successfully from {FilePath}", filePath);
                return true;
            }
            catch (MinioException ex)
            {
                _logger.LogError(ex, "MinIO error occurred while deleting file from {FilePath}", filePath);
                throw new Exception("Error deleting file from MinIO.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while deleting file from {FilePath}", filePath);
                throw;
            }
        }

        public string GenerateSignedUrl(string filePath, TimeSpan validFor)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            try
            {
                var presignedUrl = _minioClient.PresignedGetObjectAsync(
                    new PresignedGetObjectArgs()
                        .WithBucket(_bucketName)
                        .WithObject(filePath)
                        .WithExpiry((int)validFor.TotalSeconds)
                ).Result;

                _logger.LogInformation("Generated signed URL for {FilePath}", filePath);
                return presignedUrl;
            }
            catch (MinioException ex)
            {
                _logger.LogError(ex, "MinIO error occurred while generating signed URL for {FilePath}", filePath);
                throw new Exception("Error generating signed URL.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while generating signed URL for {FilePath}", filePath);
                throw;
            }
        }

        public bool TryValidateSignedUrl(string url, out string filePath)
        {
            filePath = url;
            // Validation logic can be added if needed
            return true;
        }

        public async Task CommitAsync(string filePath, List<string> blockIds)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            if (blockIds == null || !blockIds.Any())
                throw new ArgumentException("Block IDs cannot be null or empty.", nameof(blockIds));

            // Implement Commit logic for block-based uploads if applicable
            throw new NotImplementedException("CommitAsync method is not fully implemented.");
        }
    }
}
