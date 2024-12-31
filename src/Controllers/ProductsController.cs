using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using haworks.Contracts;
using haworks.Db;
using haworks.Dto;
using haworks.Models;
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
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly ILogger<ProductsController> _logger;
        private readonly IMapper _mapper;

        public ProductsController(
            IProductRepository productRepository,
            ICategoryRepository categoryRepository,
            ILogger<ProductsController> logger,
            IMapper mapper)
        {
            _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
            _categoryRepository = categoryRepository ?? throw new ArgumentNullException(nameof(categoryRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        /// <summary>
        /// List all products with paging
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts(int page = 1, int pageSize = 10)
        {
            var products = await _productRepository.GetProductsAsync(page, pageSize);
            var productDtos = _mapper.Map<List<ProductDto>>(products);
            return Ok(productDtos);
        }

        /// <summary>
        /// Retrieve a single product by Id
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<ProductDto>> GetProduct(Guid id)
        {
            var product = await _productRepository.GetProductByIdAsync(id);
            if (product == null)
                return NotFound();

            // Map to DTO
            var productDto = _mapper.Map<ProductDto>(product);

            // OPTIONAL: If you want to also retrieve content references (like images/assets),
            // you can do that in a separate call to IContentRepository or by calling /api/content/list.
            // For example:
            // var contents = await _contentRepository.GetContentsByEntityIdAsync(id, "Product");
            // productDto.Contents = _mapper.Map<List<ContentDto>>(contents);

            return Ok(productDto);
        }

        /// <summary>
        /// Create a new product. Notice we're NOT handling images/assets here.
        /// The user can call ContentController to upload images/assets after the product is created.
        /// </summary>
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<ProductDto>> CreateProduct([FromBody] ProductCreateDto productCreateDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                // 1. Check Category
                var category = await _categoryRepository.GetCategoryByIdAsync(productCreateDto.CategoryId);
                if (category == null)
                    return BadRequest("Invalid category ID.");

                // 2. Map & Create Product
                var product = _mapper.Map<haworks.Db.Product>(productCreateDto);
                product.Id = Guid.NewGuid(); // Ensure new ID
                await _productRepository.AddProductAsync(product);

                // 3. Return
                var productDto = _mapper.Map<ProductDto>(product);
                return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, productDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating product.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error.");
            }
        }

        /// <summary>
        /// Update an existing product's data (not content).
        /// Content is managed separately by ContentController.
        /// </summary>
        [HttpPut("{id}")]
        [Authorize]
        public async Task<ActionResult<ProductDto>> UpdateProduct(Guid id, [FromBody] ProductCreateDto productCreateDto)
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

            try
            {
                await _productRepository.UpdateProductAsync(product);
                var updatedDto = _mapper.Map<ProductDto>(product);
                return Ok(updatedDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating product with ID {ProductId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error.");
            }
        }

        /// <summary>
        /// Delete a product by its ID. Also consider removing associated content
        /// by calling ContentController or IContentRepository if needed.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            var product = await _productRepository.GetProductByIdAsync(id);
            if (product == null)
                return NotFound();

            // Potentially also delete content records from ContentRepository
            // e.g. var contents = await _contentRepository.GetContentsByEntityIdAsync(id, "Product");
            // _contentRepository.RemoveContents(contents);
            // _contentRepository.SaveChangesAsync(); 
            // Or let ContentController handle that in a separate request.

            try
            {
                await _productRepository.DeleteProductAsync(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product with ID {ProductId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error.");
            }
        }
    }
}
