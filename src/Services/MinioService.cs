using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;
using RitualWorks.Contracts;

namespace RitualWorks.Services
{
    public class MinioService : IFileStorageService {
    private readonly MinioClient? _minioClient;
    private readonly string _endpoint;
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly bool _secure;
        private readonly string _bucketName;

    public MinioService(IConfiguration configuration)
    {
        // Retrieve and validate configuration values
        _endpoint = configuration["MinIO:Endpoint"]
            ?? throw new ArgumentNullException("MinIO:Endpoint", "The MinIO endpoint configuration value is missing.");
        _accessKey = configuration["MinIO:AccessKey"]
            ?? throw new ArgumentNullException("MinIO:AccessKey", "The MinIO access key configuration value is missing.");
        _secretKey = configuration["MinIO:SecretKey"]
            ?? throw new ArgumentNullException("MinIO:SecretKey", "The MinIO secret key configuration value is missing.");
        _secure = bool.TryParse(configuration["MinIO:Secure"], out var parsedSecure) ? parsedSecure : true;
        _bucketName = configuration["MinIO:BucketName"]
            ?? throw new ArgumentNullException("MinIO:BucketName", "The MinIO bucket name configuration value is missing.");
        
            _minioClient = (MinioClient?)new MinioClient()
                .WithEndpoint(_endpoint)
                .WithCredentials(_accessKey, _secretKey)
                .WithSSL(_secure);

            _bucketName = configuration["MinIO:BucketName"]
                ?? throw new ArgumentNullException("MinIO:BucketName");
        }

        public async Task<string> UploadFileAsync(Stream fileStream, string filePath, bool append = false)
        {
            try
            {
                var putObjectArgs = new PutObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(filePath.Replace("\\", "/"))
                    .WithStreamData(fileStream)
                    .WithObjectSize(fileStream.Length);

                await _minioClient?.PutObjectAsync(putObjectArgs);
                return filePath; // Return the MinIO key as the URL
            }
            catch (Exception e)
            {
                throw new Exception("Error uploading file", e);
            }
        }

        public async Task<Stream> DownloadFileAsync(string filePath)
        {
            try
            {
                var getObjectArgs = new GetObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(filePath);

                var responseStream = new MemoryStream();
                await _minioClient?.GetObjectAsync(getObjectArgs, (stream) => stream.CopyTo(responseStream));
                responseStream.Position = 0;
                return responseStream;
            }
            catch (Exception e)
            {
                throw new Exception("Error downloading file", e);
            }
        }

        public async Task<bool> DeleteFileAsync(string filePath)
        {
            try
            {
                var removeObjectArgs = new RemoveObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(filePath);

                await _minioClient?.RemoveObjectAsync(removeObjectArgs);
                return true;
            }
            catch (Exception e)
            {
                throw new Exception("Error deleting file", e);
            }
        }

        public string GenerateSignedUrl(string filePath, TimeSpan validFor)
        {
            try
            {
                // Convert TimeSpan to seconds
                var presignedUrl = _minioClient?.PresignedGetObjectAsync(
                    new PresignedGetObjectArgs()
                        .WithBucket(_bucketName)
                        .WithObject(filePath)
                        .WithExpiry((int)validFor.TotalSeconds) // Convert TimeSpan to seconds
                ).Result; // Use .Result to synchronously wait for the task to complete

                return presignedUrl;
            }
            catch (Exception e)
            {
                throw new Exception("Error generating signed URL", e);
            }
        }

        public bool TryValidateSignedUrl(string url, out string filePath)
        {
            filePath = url;
            // Validation logic can be added if needed
            return true;
        }

        public Task CommitAsync(string filePath, List<string> blockIds)
        {
            throw new NotImplementedException();
        }
    }
}
