using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RitualWorks.Contracts;
using RitualWorks.Db;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
        private static readonly List<string> _allowedImageTypes = new List<string> { ".jpg", ".jpeg", ".png", ".gif" };
        private static readonly List<string> _allowedAssetTypes = new List<string> { ".pdf", ".doc", ".docx", ".zip", ".rar" };
        private const long _maxFileSize = 100 * 1024 * 1024; // 100 MB

        public AssetsController(IFileStorageService fileStorageService, IProductRepository productRepository)
        {
            _fileStorageService = fileStorageService;
            _productRepository = productRepository;
        }

        [HttpPost("upload-chunk")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadFileChunk([FromForm] FileChunkUploadDto uploadDto)
        {
            if (!ValidateFile(uploadDto.File, out string validationError, out string fileType))
            {
                return BadRequest(validationError);
            }

            string tempFolder = Path.Combine(Path.GetTempPath(), uploadDto.ProductId.ToString());
            string tempFilePath = Path.Combine(tempFolder, uploadDto.FileName);

            Directory.CreateDirectory(tempFolder);

            var fileLock = _fileLocks.GetOrAdd(uploadDto.FileName, _ => new SemaphoreSlim(1, 1));

            try
            {
                await fileLock.WaitAsync();

                using (var stream = new FileStream(tempFilePath, uploadDto.ChunkIndex == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None))
                {
                    await uploadDto.File.CopyToAsync(stream);
                }

                if (uploadDto.ChunkIndex == uploadDto.TotalChunks - 1)
                {
                    var result = await FinalizeUpload(tempFilePath, uploadDto.FileName, fileType, uploadDto.ProductId, uploadDto.Username);
                    Directory.Delete(tempFolder, true);

                    return Ok(new { FileName = result });
                }

                return Ok(new { Message = "Chunk uploaded successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { Error = "An error occurred while uploading the file chunk.", Details = ex.Message });
            }
            finally
            {
                fileLock.Release();

                if (uploadDto.ChunkIndex == uploadDto.TotalChunks - 1)
                {
                    _fileLocks.TryRemove(uploadDto.FileName, out _);
                }
            }
        }

        [HttpPost("upload")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadFile([FromForm] FileUploadDto uploadDto)
        {
            if (!ValidateFile(uploadDto.File, out string validationError, out string fileType))
            {
                return BadRequest(validationError);
            }

            var tempFilePath = Path.Combine(Path.GetTempPath(), uploadDto.File.FileName);
            var fileLock = _fileLocks.GetOrAdd(uploadDto.File.FileName, _ => new SemaphoreSlim(1, 1));

            try
            {
                await fileLock.WaitAsync();

                using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                {
                    await uploadDto.File.CopyToAsync(stream);
                }

                var result = await FinalizeUpload(tempFilePath, uploadDto.File.FileName, fileType, uploadDto.ProductId, uploadDto.Username);
                return Ok(new { FileName = result });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { Error = "An error occurred while uploading the file.", Details = ex.Message });
            }
            finally
            {
                fileLock.Release();
                _fileLocks.TryRemove(uploadDto.File.FileName, out _);
            }
        }

        private bool ValidateFile(IFormFile file, out string validationError, out string fileType)
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

            System.IO.File.Delete(tempFilePath);
            return result;
        }
    }

    // DTO for File Upload
    public class FileUploadDto
    {
        /// <summary>
        /// The file to be uploaded.
        /// </summary>
        [Required]
        public IFormFile? File { get; set; }

        /// <summary>
        /// The ID of the product associated with the file upload.
        /// </summary>
        [Required]
        public Guid ProductId { get; set; }

        /// <summary>
        /// The username of the user uploading the file.
        /// </summary>
        [Required]
        public string? Username { get; set; }
    }

    // DTO for Chunked File Upload
    public class FileChunkUploadDto
    {
        /// <summary>
        /// The file chunk being uploaded.
        /// </summary>
        [Required]
        public IFormFile? File { get; set; }

        /// <summary>
        /// The index of the current chunk.
        /// </summary>
        [Required]
        public int ChunkIndex { get; set; }

        /// <summary>
        /// The total number of chunks expected.
        /// </summary>
        [Required]
        public int TotalChunks { get; set; }

        /// <summary>
        /// The name of the file being uploaded.
        /// </summary>
        [Required]
        public string? FileName { get; set; }

        /// <summary>
        /// The ID of the product associated with the file upload.
        /// </summary>
        [Required]
        public Guid ProductId { get; set; }

        /// <summary>
        /// The username of the user uploading the file.
        /// </summary>
        [Required]
        public string? Username { get; set; }
    }
}
