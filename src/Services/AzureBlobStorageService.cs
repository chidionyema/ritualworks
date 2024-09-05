using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using RitualWorks.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace RitualWorks.Services
{
    public class AzureBlobStorageService : IFileStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _containerName;

        public AzureBlobStorageService(BlobServiceClient blobServiceClient, IConfiguration configuration)
        {
            _blobServiceClient = blobServiceClient;
            _containerName = configuration["AzureBlobStorage:ContainerName"];
        }

        public async Task<string> UploadFileAsync(Stream fileStream, string filePath, bool append = false)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await containerClient.CreateIfNotExistsAsync();

            var blobClient = containerClient.GetBlockBlobClient(filePath);

            if (append)
            {
                string blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                await blobClient.StageBlockAsync(blockId, fileStream);
                return blockId;
            }
            else
            {
                await blobClient.UploadAsync(fileStream);
                return filePath;
            }
        }

        public async Task CommitAsync(string filePath, List<string> blockIds)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlockBlobClient(filePath);
            await blobClient.CommitBlockListAsync(blockIds);
        }

        public async Task<Stream> DownloadFile(string filePath)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(filePath);
            var response = await blobClient.DownloadAsync();
            return response.Value.Content;
        }

        public async Task<bool> DeleteFileAsync(string filePath)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(filePath);
            return await blobClient.DeleteIfExistsAsync();
        }

        public string GenerateSignedUrl(string filePath, TimeSpan validFor)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(filePath);
            var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(validFor));
            return sasUri.ToString();
        }

        public bool TryValidateSignedUrl(string url, out string filePath)
        {
            filePath = url;
            return true;
        }
    }
}
