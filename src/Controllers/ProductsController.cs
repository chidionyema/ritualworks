using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AutoMapper;
using haworks.Contracts;
using haworks.Dto;
using haworks.Db;
using haworks.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace haworks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly IProductRepository _productRepository;
        private readonly IAssetService _assetService;
        private readonly ILogger<ProductsController> _logger;
        private readonly IMapper _mapper;

        public ProductsController(
            ICategoryRepository categoryRepository,
            IProductRepository productRepository,
            IAssetService assetService,
            ILogger<ProductsController> logger,
            IMapper mapper)
        {
            _categoryRepository = categoryRepository ?? throw new ArgumentNullException(nameof(categoryRepository));
            _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        #region Product Endpoints

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts(int page = 1, int pageSize = 10)
        {
            var products = await _productRepository.GetProductsAsync(page, pageSize);
            var productDtos = _mapper.Map<List<ProductDto>>(products);
            return Ok(productDtos);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ProductDto>> GetProduct(Guid id)
        {
            var product = await _productRepository.GetProductByIdAsync(id);

            if (product == null)
                return NotFound();

            var productDto = _mapper.Map<ProductDto>(product);
            return Ok(productDto);
        }

        [HttpGet("categories/{categoryId}")]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetProductsByCategory(Guid categoryId, int page = 1, int pageSize = 10)
        {
            var products = await _productRepository.GetProductsByCategoryAsync(categoryId, page, pageSize);
            var productDtos = _mapper.Map<List<ProductDto>>(products);
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

                // Map the incoming DTO to a Product entity
                var product = _mapper.Map<Product>(productCreateDto);

                // Add the product to the database
                await _productRepository.AddProductAsync(product);
                await _productRepository.SaveChangesAsync(); // Save to get the generated product.Id

                // Handle image uploads
                if (productCreateDto.Images != null && productCreateDto.Images.Count > 0)
                {
                    var uploadedImages = await UploadProductImages(productCreateDto.Images, product.Id, productCreateDto.Name);
                    await _productRepository.AddProductImagesAsync(uploadedImages);
                }

                // Handle asset uploads
                if (productCreateDto.Assets != null && productCreateDto.Assets.Count > 0)
                {
                    var uploadedAssets = await UploadProductAssets(productCreateDto.Assets, product.Id, productCreateDto.Name);
                    await _productRepository.AddProductAssetsAsync(uploadedAssets);
                }

                // Save all changes
                await _productRepository.SaveChangesAsync();

                var productDto = _mapper.Map<ProductDto>(product);
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
        public async Task<ActionResult<ProductDto>> UpdateProduct(Guid id, [FromForm] ProductCreateDto productCreateDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var product = await _productRepository.GetProductByIdAsync(id);

            if (product == null)
                return NotFound();

            var category = await _categoryRepository.GetCategoryByIdAsync(productCreateDto.CategoryId);
            if (category == null)
                return BadRequest("Invalid category ID.");

            // Map the updated values onto the existing product entity
            _mapper.Map(productCreateDto, product);

            // Handle image uploads
            if (productCreateDto.Images != null && productCreateDto.Images.Count > 0)
            {
                // Delete existing images
                _productRepository.RemoveProductImages(product.ProductImages);

                var uploadedImages = await UploadProductImages(productCreateDto.Images, product.Id, productCreateDto.Name);
                await _productRepository.AddProductImagesAsync(uploadedImages);
            }

            // Handle asset uploads
            if (productCreateDto.Assets != null && productCreateDto.Assets.Count > 0)
            {
                // Delete existing assets
                _productRepository.RemoveProductAssets(product.ProductAssets);

                var uploadedAssets = await UploadProductAssets(productCreateDto.Assets, product.Id, productCreateDto.Name);
                await _productRepository.AddProductAssetsAsync(uploadedAssets);
            }

            await _productRepository.UpdateProductAsync(product);
            await _productRepository.SaveChangesAsync();

            var updatedProductDto = _mapper.Map<ProductDto>(product);
            return Ok(updatedProductDto);
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            var product = await _productRepository.GetProductByIdAsync(id);

            if (product == null)
                return NotFound();

            // Delete associated images and assets
            _productRepository.RemoveProductImages(product.ProductImages);
            _productRepository.RemoveProductAssets(product.ProductAssets);

            await _productRepository.DeleteProductAsync(id);
            await _productRepository.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("categories")]
        public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories()
        {
            var categories = await _categoryRepository.GetCategoriesAsync();
            var categoryDtos = _mapper.Map<List<CategoryDto>>(categories);
            return Ok(categoryDtos);
        }

        [HttpPost("categories")]
        [Authorize]
        public async Task<ActionResult<CategoryDto>> CreateCategory([FromBody] CategoryDto categoryDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var category = _mapper.Map<Category>(categoryDto);

            await _categoryRepository.AddCategoryAsync(category);
            await _categoryRepository.SaveChangesAsync();

            var createdCategoryDto = _mapper.Map<CategoryDto>(category);
            return CreatedAtAction(nameof(GetCategories), new { id = category.Id }, createdCategoryDto);
        }

        #endregion

        #region Helper Methods

        private async Task<List<ProductImage>> UploadProductImages(List<IFormFile> images, Guid productId, string username)
        {
            var uploadedImages = new List<ProductImage>();

            foreach (var image in images)
            {
                var imageUrl = await _assetService.UploadFileAsync(image, productId, username);
                var productImage = new ProductImage
                {
                    Url = imageUrl,
                    BlobName = Path.GetFileName(imageUrl),
                    ProductId = productId
                };
                uploadedImages.Add(productImage);
            }

            return uploadedImages;
        }

        private async Task<List<ProductAsset>> UploadProductAssets(List<IFormFile> assets, Guid productId, string username)
        {
            var uploadedAssets = new List<ProductAsset>();

            foreach (var asset in assets)
            {
                var assetUrl = await _assetService.UploadFileAsync(asset, productId, username);
                var productAsset = new ProductAsset
                {
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
}
