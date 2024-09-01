using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Configuration;
using RitualWorks.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace RitualWorks.Services
{
    public class S3Service : IFileStorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;

        public S3Service(IAmazonS3 s3Client, IConfiguration configuration)
        {
            _s3Client = s3Client;
            _bucketName = configuration["AWS:BucketName"];
        }

        public async Task<string> UploadFileAsync(Stream fileStream, string filePath, bool append = false)
        {
            var uploadRequest = new TransferUtilityUploadRequest
            {
                InputStream = fileStream,
                Key = filePath.Replace("\\", "/"),
                BucketName = _bucketName,
                PartSize = 5 * 1024 * 1024, // 5 MB.
                AutoCloseStream = false
            };

            using (var transferUtility = new TransferUtility(_s3Client))
            {
                await transferUtility.UploadAsync(uploadRequest);
            }

            return filePath; // Return the S3 key as the URL
        }

        public async Task<Stream> DownloadFileAsync(string filePath)
        {
            var getRequest = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = filePath
            };
            var response = await _s3Client.GetObjectAsync(getRequest);
            return response.ResponseStream;
        }

        public async Task<bool> DeleteFileAsync(string filePath)
        {
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = filePath
            };
            var response = await _s3Client.DeleteObjectAsync(deleteRequest);
            return response.HttpStatusCode == System.Net.HttpStatusCode.NoContent;
        }

        public string GenerateSignedUrl(string filePath, TimeSpan validFor)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = filePath,
                Expires = DateTime.UtcNow.Add(validFor)
            };
            var presignedUrl = _s3Client.GetPreSignedURL(request);
            return presignedUrl;
        }

        public bool TryValidateSignedUrl(string url, out string filePath)
        {
            filePath = url;
            return true;
        }

        public Task CommitAsync(string filePath, List<string> blockIds)
        {
            // Implement if needed for S3. Typically not required for simple use cases.
            throw new NotImplementedException();
        }
    }
}
