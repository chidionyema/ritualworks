using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RitualWorks.Contracts;
using RitualWorks.Db;
using RitualWorks.Services;

namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IAssetService _assetService;
        private readonly ILogger<ProductsController> _logger;

        public ProductsController(
            IProductRepository productRepository,
            ICategoryRepository categoryRepository,
            IAssetService assetService,
            ILogger<ProductsController> logger)
        {
            _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
            _categoryRepository = categoryRepository ?? throw new ArgumentNullException(nameof(categoryRepository));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Product Endpoints

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts(int page = 1, int pageSize = 10)
        {
            var products = await _productRepository.GetProductsAsync(page, pageSize);
            var productDtos = products.Select(MapToProductDto).ToList();
            return Ok(productDtos);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ProductDto>> GetProduct(Guid id)
        {
            var product = await _productRepository.GetProductByIdAsync(id);

            if (product == null)
            {
                return NotFound();
            }

            var productDto = MapToProductDto(product);
            return Ok(productDto);
        }

        [HttpGet("categories/{categoryId}")]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetProductsByCategory(Guid categoryId, int page = 1, int pageSize = 10)
        {
            var products = await _productRepository.GetProductsByCategoryAsync(categoryId, page, pageSize);
            var productDtos = products.Select(MapToProductDto).ToList();
            return Ok(productDtos);
        }

   [HttpPost]
[Authorize]
public async Task<ActionResult<ProductDto>> CreateProduct([FromForm] ProductCreateDto productCreateDto)
{
    if (!ModelState.IsValid)
        return BadRequest(ModelState);

    try
    {
        // Ensure the category exists
        var category = await _categoryRepository.GetCategoryByIdAsync(productCreateDto.CategoryId);
        if (category == null)
            return BadRequest("Invalid category ID.");

        // Map the incoming DTO to a Product entity and generate a new Product ID
        var product = MapToProduct(productCreateDto);
        product.Id = Guid.NewGuid();

        // Add the product to the database first, so it exists before adding images or assets
        await _productRepository.AddProductAsync(product);

        // Fetch the product from the database to ensure it's the latest version
        product = await _productRepository.GetProductByIdAsync(product.Id);

        // Handle image uploads after adding the product
        if (productCreateDto.Images != null && productCreateDto.Images.Count > 0)
        {
            var uploadedImages = await UploadProductImages(productCreateDto.Images, product.Id, productCreateDto.Name);
            product.ProductImages.AddRange(uploadedImages);
        }

        // Handle asset uploads after adding the product
        if (productCreateDto.Assets != null && productCreateDto.Assets.Count > 0)
        {
            var uploadedAssets = await UploadProductAssets(productCreateDto.Assets, product.Id, productCreateDto.Name);
            product.ProductAssets.AddRange(uploadedAssets);
        }

        // Update the product with the newly uploaded images and assets
        await _productRepository.UpdateProductAsync(product);

        // Map the saved product to DTO for returning
        var productDto = MapToProductDto(product);
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, productDto);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error creating product.");
        return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error.");
    }
}


        [HttpPut("{id}")]
        [Authorize]
        public async Task<ActionResult<ProductDto>> UpdateProduct(Guid id, [FromBody] ProductDto productDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var product = await _productRepository.GetProductByIdAsync(id);
            if (product == null)
                return NotFound();

            var category = await _categoryRepository.GetCategoryByIdAsync(productDto.CategoryId);
            if (category == null)
                return BadRequest("Invalid category ID.");

            // Update product properties
            product.Name = productDto.Name;
            product.Description = productDto.Description;
            product.Price = productDto.Price;
            product.Stock = productDto.Stock;
            product.Rating = productDto.Rating;
            product.IsNew = productDto.IsNew;
            product.InStock = productDto.InStock;
            product.Brand = productDto.Brand;
            product.Type = productDto.Type;
            product.CategoryId = productDto.CategoryId;

            await _productRepository.UpdateProductAsync(product);
            var updatedProductDto = MapToProductDto(product);
            return Ok(updatedProductDto);
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            var product = await _productRepository.GetProductByIdAsync(id);
            if (product == null)
                return NotFound();

            await _productRepository.DeleteProductAsync(product.Id);
            return NoContent();
        }

        [HttpGet("categories")]
        public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories()
        {
            var categories = await _categoryRepository.GetCategoriesAsync();
            var categoryDtos = categories.Select(MapToCategoryDto).ToList();
            return Ok(categoryDtos);
        }

        [HttpPost("categories")]
        [Authorize]
        public async Task<ActionResult<CategoryDto>> CreateCategory([FromBody] CategoryDto categoryDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var category = new Category(Guid.NewGuid(), categoryDto.Name);
            await _categoryRepository.AddCategoryAsync(category);
            var createdCategoryDto = MapToCategoryDto(category);
            return CreatedAtAction(nameof(GetCategories), new { id = category.Id }, createdCategoryDto);
        }

        #endregion

        #region Helper Methods

        private ProductDto MapToProductDto(Product product)
        {
            return new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Stock = product.Stock,
                Rating = product.Rating,
                IsNew = product.IsNew,
                InStock = product.InStock,
                Brand = product.Brand,
                Type = product.Type,
                CategoryId = product.CategoryId,
                ProductImages = product.ProductImages?.Select(image => new ProductImageDto
                {
                    Id = image.Id,
                    Url = image.Url,
                    BlobName = image.BlobName,
                    ProductId = image.ProductId
                }).ToList(),
                ProductAssets = product.ProductAssets?.Select(asset => new ProductAssetDto
                {
                    Id = asset.Id,
                    AssetUrl = asset.AssetUrl,
                    BlobName = asset.BlobName,
                    ProductId = asset.ProductId
                }).ToList()
            };
        }

        private Product MapToProduct(ProductCreateDto productCreateDto)
        {
            return new Product
            {
                Name = productCreateDto.Name,
                Description = productCreateDto.Description,
                Price = productCreateDto.Price,
                Stock = productCreateDto.Stock,
                Rating = productCreateDto.Rating,
                IsNew = productCreateDto.IsNew,
                InStock = productCreateDto.InStock,
                Brand = productCreateDto.Brand,
                Type = productCreateDto.Type,
                CategoryId = productCreateDto.CategoryId
            };
        }

        private CategoryDto MapToCategoryDto(Category category)
        {
            return new CategoryDto
            {
                Id = category.Id,
                Name = category.Name
            };
        }

        private async Task<List<ProductImage>> UploadProductImages(List<IFormFile> images, Guid productId, string productName)
        {
            var uploadedImages = new List<ProductImage>();

            foreach (var image in images)
            {
                var imageUrl = await _assetService.UploadFileAsync(image, productId, productName);
                var productImage = new ProductImage
                {
                    Id = Guid.NewGuid(),
                    Url = imageUrl,
                    BlobName = Path.GetFileName(imageUrl),
                    ProductId = productId
                };
                uploadedImages.Add(productImage);
            }

            return uploadedImages;
        }

        private async Task<List<ProductAsset>> UploadProductAssets(List<IFormFile> assets, Guid productId, string productName)
        {
            var uploadedAssets = new List<ProductAsset>();

            foreach (var asset in assets)
            {
                var assetUrl = await _assetService.UploadFileAsync(asset, productId, productName);
                var productAsset = new ProductAsset
                {
                    Id = Guid.NewGuid(),
                    AssetUrl = assetUrl,
                    BlobName = Path.GetFileName(assetUrl),
                    ProductId = productId
                };
                uploadedAssets.Add(productAsset);
            }

            return uploadedAssets;
        }

        #endregion
    }

    #region DTOs

    public class ProductDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public double Rating { get; set; }
        public bool IsNew { get; set; }
        public bool InStock { get; set; }
        public string? Brand { get; set; }
        public string? Type { get; set; }
        public Guid CategoryId { get; set; }
        public List<ProductImageDto>? ProductImages { get; set; }
        public List<ProductAssetDto>? ProductAssets { get; set; }
    }

    public class ProductCreateDto
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public double Rating { get; set; }
        public bool IsNew { get; set; }
        public bool InStock { get; set; }
        public string? Brand { get; set; }
        public string? Type { get; set; }
        public Guid CategoryId { get; set; }
        public List<IFormFile> Images { get; set; }
        public List<IFormFile> Assets { get; set; }
    }

    public class ProductImageDto
    {
        public Guid Id { get; set; }
        public string Url { get; set; }
        public string BlobName { get; set; }
        public Guid ProductId { get; set; }
    }

    public class ProductAssetDto
    {
        public Guid Id { get; set; }
        public string AssetUrl { get; set; }
        public string BlobName { get; set; }
        public Guid ProductId { get; set; }
    }

    public class CategoryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    #endregion
}
