using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RitualWorks.Contracts;
using RitualWorks.Db;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RitualWorks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AssetsController : ControllerBase
    {
        private readonly IFileStorageService _fileStorageService;
        private readonly IProductRepository _productRepository;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
        private static readonly List<string> _allowedImageTypes = new() { ".jpg", ".jpeg", ".png", ".gif" };
        private static readonly List<string> _allowedAssetTypes = new() { ".pdf", ".doc", ".docx", ".zip", ".rar" };
        private const long _maxFileSize = 100 * 1024 * 1024; // 100 MB

        public AssetsController(IFileStorageService fileStorageService, IProductRepository productRepository)
        {
            _fileStorageService = fileStorageService;
            _productRepository = productRepository;
        }

        [HttpPost("upload-chunk")]
        public async Task<IActionResult> UploadFileChunk([FromForm] IFormFile file, [FromForm] int chunkIndex, [FromForm] int totalChunks, [FromForm] string fileName, [FromForm] Guid productId, [FromForm] string username)
        {
            if (!ValidateFile(file, out string validationError, out string fileType))
            {
                return BadRequest(validationError);
            }

            var tempFilePath = Path.Combine(Path.GetTempPath(), fileName);
            var fileLock = _fileLocks.GetOrAdd(fileName, _ => new SemaphoreSlim(1, 1));

            try
            {
                await fileLock.WaitAsync();
                using var stream = new FileStream(tempFilePath, chunkIndex == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write);
                await file.CopyToAsync(stream);

                if (chunkIndex == totalChunks - 1)
                {
                    var result = await FinalizeUpload(tempFilePath, fileName, fileType, productId, username);
                    return Ok(new { FileName = result });
                }

                return Ok();
            }
            finally
            {
                fileLock.Release();
                if (chunkIndex == totalChunks - 1)
                {
                    _fileLocks.TryRemove(fileName, out _);
                }
            }
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile([FromForm] IFormFile file, [FromForm] Guid productId, [FromForm] string username)
        {
            if (!ValidateFile(file, out string validationError, out string fileType))
            {
                return BadRequest(validationError);
            }

            var tempFilePath = Path.Combine(Path.GetTempPath(), file.FileName);
            var fileLock = _fileLocks.GetOrAdd(file.FileName, _ => new SemaphoreSlim(1, 1));

            try
            {
                await fileLock.WaitAsync();

                using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                {
                    await file.CopyToAsync(stream);
                }

                var result = await FinalizeUpload(tempFilePath, file.FileName, fileType, productId, username);
                return Ok(new { FileName = result });
            }
            finally
            {
                fileLock.Release();
                _fileLocks.TryRemove(file.FileName, out _);
            }
        }

        private bool ValidateFile(IFormFile file, out string validationError, out string fileType)
        {
            validationError = string.Empty;
            fileType = string.Empty;

            if (file == null || file.Length == 0)
            {
                validationError = "No file uploaded";
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
                fileType = "products/images";
                return true;
            }
            else if (_allowedAssetTypes.Contains(fileExtension))
            {
                fileType = "products/assets";
                return true;
            }
            else
            {
                validationError = "Invalid file type.";
                return false;
            }
        }

        private async Task<string> FinalizeUpload(string tempFilePath, string fileName, string type, Guid productId, string username)
        {
            using var stream = new FileStream(tempFilePath, FileMode.Open);
            var filePath = $"{username}/{type}/{fileName}";
            var result = await _fileStorageService.UploadFileAsync(stream, filePath, append: false);

            if (type.Contains("images"))
            {
                var productImage = new ProductImage(Guid.NewGuid(), productId, null)
                {
                    Url = result,
                    BlobName = fileName
                };
                await _productRepository.AddProductImageAsync(productImage);
            }
            else
            {
                var productAsset = new ProductAsset(Guid.NewGuid(), productId, null)
                {
                    AssetUrl = result,
                    BlobName = fileName
                };
                await _productRepository.AddProductAssetAsync(productAsset);
            }

            System.IO.File.Delete(tempFilePath); // Delete the temp file after uploading
            return result;
        }
    }
}
