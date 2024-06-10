using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using System;

namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BlobController : ControllerBase
    {
        private readonly BlobSettings _blobSettings;

        public BlobController(IOptions<BlobSettings> blobSettings)
        {
            _blobSettings = blobSettings.Value;
        }

        [HttpGet("generate-sas-token")]
        public IActionResult GenerateSasToken(string blobName)
        {
            var blobServiceClient = new BlobServiceClient(_blobSettings.ConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(_blobSettings.ContainerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerClient.Name,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1) // Token expiry time
            };

            sasBuilder.SetPermissions(BlobContainerSasPermissions.Write);

            var sasToken = blobClient.GenerateSasUri(sasBuilder);

            return Ok(new { uri = sasToken });
        }
    }

    public class BlobSettings
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
    }
}
