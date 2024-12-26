using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly IContentRepository _contentRepository;
        private readonly IContentService _assetService;
        private readonly ILogger<ProductsController> _logger;
        private readonly IMapper _mapper;

        public ProductsController(
            ICategoryRepository categoryRepository,
            IProductRepository productRepository,
            IContentRepository contentRepository,
            IContentService assetService,
            ILogger<ProductsController> logger,
            IMapper mapper)
        {
            _categoryRepository = categoryRepository ?? throw new ArgumentNullException(nameof(categoryRepository));
            _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
            _contentRepository = contentRepository ?? throw new ArgumentNullException(nameof(contentRepository));
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

            var content = await _contentRepository.GetContentsByEntityIdAsync(id, "Product");
            var productDto = _mapper.Map<ProductDto>(product);
            productDto.Contents = _mapper.Map<List<ContentDto>>(content);
            return Ok(productDto);
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
                await _productRepository.SaveChangesAsync();

                // Handle content uploads
                var contents = await UploadContent(productCreateDto, product.Id);
                if (contents.Any())
                {
                    await _contentRepository.AddContentsAsync(contents);
                    await _contentRepository.SaveChangesAsync();
                }

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

            // Update content
            var existingContents = await _contentRepository.GetContentsByEntityIdAsync(product.Id, "Product");
            _contentRepository.RemoveContents(existingContents);

            var updatedContents = await UploadContent(productCreateDto, product.Id);
            await _contentRepository.AddContentsAsync(updatedContents);

            await _productRepository.UpdateProductAsync(product);
            await _productRepository.SaveChangesAsync();
            await _contentRepository.SaveChangesAsync();

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

            // Delete associated content
            var contents = await _contentRepository.GetContentsByEntityIdAsync(product.Id, "Product");
            _contentRepository.RemoveContents(contents);

            await _productRepository.DeleteProductAsync(id);
            await _productRepository.SaveChangesAsync();

            return NoContent();
        }

        #endregion

        #region Helper Methods

        private async Task<List<Content>> UploadContent(ProductCreateDto productCreateDto, Guid productId)
        {
            var contents = new List<Content>();

            // Upload images
            if (productCreateDto.Images != null)
            {
                foreach (var image in productCreateDto.Images)
                {
                    var imageUrl = await _assetService.UploadFileAsync(image, productId, productCreateDto.Name);
                    contents.Add(new Content
                    {
                        Id = Guid.NewGuid(),
                        EntityId = productId,
                        EntityType = "Product",
                        ContentType = ContentType.Image,
                        Url = imageUrl,
                        BlobName = Path.GetFileName(imageUrl),
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // Upload assets
            if (productCreateDto.Assets != null)
            {
                foreach (var asset in productCreateDto.Assets)
                {
                    var assetUrl = await _assetService.UploadFileAsync(asset, productId, productCreateDto.Name);
                    contents.Add(new Content
                    {
                        Id = Guid.NewGuid(),
                        EntityId = productId,
                        EntityType = "Product",
                        ContentType = ContentType.Asset,
                        Url = assetUrl,
                        BlobName = Path.GetFileName(assetUrl),
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            return contents;
        }

        #endregion
    }
}
