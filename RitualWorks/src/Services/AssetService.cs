public class AssetService : IAssetService
{
    private readonly MinioClient _minioClient;
    private readonly IProductRepository _productRepository;
    private readonly ILogger<AssetService> _logger;
    private static readonly List<string> _allowedImageTypes = new List<string> { ".jpg", ".jpeg", ".png", ".gif" };
    private static readonly List<string> _allowedAssetTypes = new List<string> { ".pdf", ".doc", ".docx", ".zip", ".rar" };
    private const long _maxFileSize = 100 * 1024 * 1024; // 100 MB
    private readonly string _bucketName = "ritualworks-bucket"; // Replace with your bucket name

    public AssetService(MinioClient minioClient, IProductRepository productRepository, ILogger<AssetService> logger)
    {
        _minioClient = minioClient ?? throw new ArgumentNullException(nameof(minioClient));
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
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

        var tempFilePath = Path.Combine(Path.GetTempPath(), file.FileName);
        _logger.LogInformation("Writing file to temp path: {TempFilePath}", tempFilePath);

        // Write file to temporary location
        await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
        {
            await file.CopyToAsync(stream);
        }

        _logger.LogInformation("Finalizing upload for file: {TempFilePath}", tempFilePath);
        return await FinalizeUploadAsync(tempFilePath, file.FileName, productId, username);
    }

    private async Task<string> FinalizeUploadAsync(string tempFilePath, string fileName, Guid productId, string username)
    {
        var fileType = Path.GetExtension(fileName).ToLowerInvariant();
        var type = _allowedImageTypes.Contains(fileType) ? "productimages" : "productassets";
        var objectName = $"{username}/{type}/{fileName}";

        await using (var stream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
        {
            try
            {
                _logger.LogInformation("Uploading file to MinIO: {ObjectName}", objectName);

                var putObjectArgs = new PutObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(objectName)
                    .WithStreamData(stream)
                    .WithObjectSize(stream.Length)
                    .WithContentType(GetContentType(fileName)); // Use inferred content type

                await _minioClient.PutObjectAsync(putObjectArgs);
            }
            catch (MinioException ex)
            {
                _logger.LogError(ex, "Error occurred while uploading to MinIO: {Message}", ex.Message);
                throw new Exception("Failed to upload to MinIO.", ex);
            }
        }

        // Ensure temp file is deleted even in case of an error
        File.Delete(tempFilePath);

        var resultUrl = $"https://{_bucketName}/{objectName}";

        if (type.Contains("images", StringComparison.OrdinalIgnoreCase))
        {
            var productImage = new ProductImage(Guid.NewGuid(), productId, resultUrl) { BlobName = fileName };
            await _productRepository.AddProductImageAsync(productImage);
        }
        else
        {
            var productAsset = new ProductAsset(Guid.NewGuid(), productId, resultUrl) { BlobName = fileName };
            await _productRepository.AddProductAssetAsync(productAsset);
        }

        return resultUrl;
    }

    private string GetContentType(string fileName)
    {
        return fileName switch
        {
            string ext when ext.EndsWith(".jpg") || ext.EndsWith(".jpeg") => "image/jpeg",
            string ext when ext.EndsWith(".png") => "image/png",
            string ext when ext.EndsWith(".gif") => "image/gif",
            string ext when ext.EndsWith(".pdf") => "application/pdf",
            string ext when ext.EndsWith(".doc") || ext.EndsWith(".docx") => "application/msword",
            string ext when ext.EndsWith(".zip") || ext.EndsWith(".rar") => "application/zip",
            _ => "application/octet-stream"
        };
    }
}
