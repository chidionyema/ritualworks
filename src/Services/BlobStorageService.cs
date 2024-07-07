using System;
using Azure.Storage.Blobs;
using Stripe;
using System.IO;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using RitualWorks.Contracts;
using Microsoft.Extensions.Options;
using RitualWorks.Controllers;

namespace RitualWorks.Services
{

    public class BlobStorageService : IBlobStorageService
    {
        private readonly BlobSettings _blobSettings;
        private string _containerName;

        public BlobStorageService(IOptions<BlobSettings> blobSettings)
        {
            _blobSettings = blobSettings.Value;
        }
        public async Task CombineChunks(string uploadPath, string fileName, int totalChunks)
        {
            var finalPath = Path.Combine(uploadPath, fileName);
            using (var finalStream = new FileStream(finalPath, FileMode.Create))
            {
                for (int i = 1; i <= totalChunks; i++)
                {
                    var chunkPath = Path.Combine(uploadPath, $"{fileName}.part{i}");
                    using (var chunkStream = new FileStream(chunkPath, FileMode.Open))
                    {
                        await chunkStream.CopyToAsync(finalStream);
                    }
                }
            }
            // Optionally upload to blob storage
            await UploadToBlobStorage(finalPath, fileName);
        }

        public async Task UploadToBlobStorage(string filePath, string blobName)
        {
            var blobServiceClient = new BlobServiceClient(_blobSettings.ConnectionString);
            var blobContainerClient = blobServiceClient.GetBlobContainerClient(_blobSettings.ContainerName);
            var blobClient = blobContainerClient.GetBlobClient(blobName);

            using (var fileStream = System.IO.File.OpenRead(filePath))
            {
                await blobClient.UploadAsync(fileStream, true);
            }
        }
    }
}

