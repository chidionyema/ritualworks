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
using RitualWorks.Repositories;
using RitualWorks.Services;

namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        // Dependencies Injection
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
            var productDtos = products.Select(product => new ProductDto
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
            }).ToList();

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

            var productDto = new ProductDto
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

            return Ok(productDto);
        }

        /// <summary>
        /// Creates a new product along with uploading images and assets.
        /// </summary>
        [HttpPost]
        [Authorize] // Ensure only authenticated users can create products
        public async Task<ActionResult<ProductDto>> CreateProduct([FromForm] ProductCreateDto productCreateDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                // Verify Category Exists
                var category = await _categoryRepository.GetCategoryByIdAsync(productCreateDto.CategoryId);
                if (category == null)
                    return BadRequest("Invalid category ID.");

                // Manually map DTO to Product entity
                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    Name = productCreateDto.Name,
                    Description = productCreateDto.Description,
                    Price = productCreateDto.Price,
                    Stock = productCreateDto.Stock,
                    Rating = productCreateDto.Rating,
                    IsNew = productCreateDto.IsNew,
                    InStock = productCreateDto.InStock,
                    Brand = productCreateDto.Brand,
                    Type = productCreateDto.Type,
                    CategoryId = productCreateDto.CategoryId,
                    ProductImages = new List<ProductImage>(),
                    ProductAssets = new List<ProductAsset>()
                };

                // Add Product to Repository first
                await _productRepository.AddProductAsync(product);

                // Upload Images
                if (productCreateDto.Images != null && productCreateDto.Images.Count > 0)
                {
                    var uploadedImages = await UploadProductImages(productCreateDto.Images, product.Id, productCreateDto.Name);
                    product.ProductImages.AddRange(uploadedImages);
                }

                // Upload Assets
                if (productCreateDto.Assets != null && productCreateDto.Assets.Count > 0)
                {
                    var uploadedAssets = await UploadProductAssets(productCreateDto.Assets, product.Id, productCreateDto.Name);
                    product.ProductAssets.AddRange(uploadedAssets);
                }

                // Update Product with Images and Assets
                await _productRepository.UpdateProductAsync(product);

                // Manually map Product to ProductDto
                var productDto = new ProductDto
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

                // Return Created Response
                return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, productDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating product.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error.");
            }
        }

        #endregion
    }
}
