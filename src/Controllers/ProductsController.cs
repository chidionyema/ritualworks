using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper; // Added for AutoMapper
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        private readonly RitualWorksContext _context;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IAssetService _assetService;
        private readonly ILogger<ProductsController> _logger;
        private readonly IMapper _mapper; // Added for AutoMapper

        public ProductsController(
            RitualWorksContext context,
            ICategoryRepository categoryRepository,
            IAssetService assetService,
            ILogger<ProductsController> logger,
            IMapper mapper) // Injected IMapper
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _categoryRepository = categoryRepository ?? throw new ArgumentNullException(nameof(categoryRepository));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        #region Product Endpoints

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts(int page = 1, int pageSize = 10)
        {
            var products = await _context.Products
                .Include(p => p.ProductImages)
                .Include(p => p.ProductAssets)
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var productDtos = _mapper.Map<List<ProductDto>>(products);
            return Ok(productDtos);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ProductDto>> GetProduct(Guid id)
        {
            var product = await _context.Products
                .Include(p => p.ProductImages)
                .Include(p => p.ProductAssets)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
                return NotFound();

            var productDto = _mapper.Map<ProductDto>(product);
            return Ok(productDto);
        }

        [HttpGet("categories/{categoryId}")]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetProductsByCategory(Guid categoryId, int page = 1, int pageSize = 10)
        {
            var products = await _context.Products
                .Include(p => p.ProductImages)
                .Include(p => p.ProductAssets)
                .Where(p => p.CategoryId == categoryId)
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var productDtos = _mapper.Map<List<ProductDto>>(products);
            return Ok(productDtos);
        }

        [HttpPost]
        [Authorize]
        public async Task<ActionResult<ProductDto>> CreateProduct([FromForm] ProductCreateDto productCreateDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Ensure the category exists
                var category = await _categoryRepository.GetCategoryByIdAsync(productCreateDto.CategoryId);
                if (category == null)
                    return BadRequest("Invalid category ID.");

                // Map the incoming DTO to a Product entity
                var product = _mapper.Map<Product>(productCreateDto);

                // Add the product to the database
                await _context.Products.AddAsync(product);
                await _context.SaveChangesAsync(); // Save to get the generated product.Id

                // Handle image uploads
                if (productCreateDto.Images != null && productCreateDto.Images.Count > 0)
                {
                    var uploadedImages = await UploadProductImages(productCreateDto.Images, product.Id, productCreateDto.Name);
                    _context.ProductImages.AddRange(uploadedImages);
                }

                // Handle asset uploads
                if (productCreateDto.Assets != null && productCreateDto.Assets.Count > 0)
                {
                    var uploadedAssets = await UploadProductAssets(productCreateDto.Assets, product.Id, productCreateDto.Name);
                    _context.ProductAssets.AddRange(uploadedAssets);
                }

                // Save all changes
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var productDto = _mapper.Map<ProductDto>(product);
                return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, productDto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
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

            var product = await _context.Products
                .Include(p => p.ProductImages)
                .Include(p => p.ProductAssets)
                .FirstOrDefaultAsync(p => p.Id == id);

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
                // Optionally, delete existing images
                _context.ProductImages.RemoveRange(product.ProductImages);

                var uploadedImages = await UploadProductImages(productCreateDto.Images, product.Id, productCreateDto.Name);
                _context.ProductImages.AddRange(uploadedImages);
            }

            // Handle asset uploads
            if (productCreateDto.Assets != null && productCreateDto.Assets.Count > 0)
            {
                // Optionally, delete existing assets
                _context.ProductAssets.RemoveRange(product.ProductAssets);

                var uploadedAssets = await UploadProductAssets(productCreateDto.Assets, product.Id, productCreateDto.Name);
                _context.ProductAssets.AddRange(uploadedAssets);
            }

            await _context.SaveChangesAsync();

            var updatedProductDto = _mapper.Map<ProductDto>(product);
            return Ok(updatedProductDto);
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            var product = await _context.Products
                .Include(p => p.ProductImages)
                .Include(p => p.ProductAssets)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
                return NotFound();

            // Optionally, delete associated images and assets
            _context.ProductImages.RemoveRange(product.ProductImages);
            _context.ProductAssets.RemoveRange(product.ProductAssets);

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

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

            await _context.Categories.AddAsync(category);
            await _context.SaveChangesAsync();

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
        public string Brand { get; set; }
        public string Type { get; set; }
        public Guid CategoryId { get; set; }
        public List<ProductImageDto> ProductImages { get; set; }
        public List<ProductAssetDto> ProductAssets { get; set; }
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
        public string Brand { get; set; }
        public string Type { get; set; }
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
        public string Name { get; set; }
    }

    #endregion
}
