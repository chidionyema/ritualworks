using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RitualWorks.Services;
using Microsoft.AspNetCore.Authorization;
namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UploadController : ControllerBase
    {
        private readonly IBlobStorageService _blobStorageService;

        public UploadController(IBlobStorageService blobStorageService)
        {
            _blobStorageService = blobStorageService;
        }

        [HttpPost("chunk")]
        [Authorize]
        public async Task<IActionResult> UploadChunk([FromForm] IFormFile chunk, [FromForm] int chunkNumber, [FromForm] int totalChunks, [FromForm] string fileName)
        {
            var uploadPath = Path.Combine("uploads", fileName);
            var filePath = Path.Combine(uploadPath, chunkNumber == 1 ? $"{fileName}.part" : $"{fileName}.part{chunkNumber}");
            if (!Directory.Exists(uploadPath))
            {
                Directory.CreateDirectory(uploadPath);
            }

            using (var stream = new FileStream(filePath, chunkNumber == 1 ? FileMode.Create : FileMode.Append))
            {
                await chunk.CopyToAsync(stream);
            }

            if (chunkNumber == totalChunks)
            {
                await _blobStorageService.CombineChunks(uploadPath, fileName, totalChunks);
            }

            return Ok(new { message = "Chunk uploaded successfully." });
        }
    }
}
