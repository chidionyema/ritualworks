using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using RitualWorks.Contracts;
using RitualWorks.Db;
using RitualWorks.Repositories;
using Microsoft.AspNetCore.Authorization;
namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IMapper _mapper;

        public ProductsController(IProductRepository productRepository, ICategoryRepository categoryRepository, IMapper mapper)
        {
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
            _mapper = mapper;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts(int page = 1, int pageSize = 10)
        {
            var products = await _productRepository.GetProductsAsync(page, pageSize);
            var productDtos = _mapper.Map<IEnumerable<ProductDto>>(products);
            return Ok(productDtos);
        }

        [HttpGet("categories/{categoryId}")]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetProductsByCategory(Guid categoryId, int page = 1, int pageSize = 10)
        {
            var products = await _productRepository.GetProductsByCategoryAsync(categoryId, page, pageSize);
            var productDtos = _mapper.Map<IEnumerable<ProductDto>>(products);
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

            var productDto = _mapper.Map<ProductDto>(product);
            return Ok(productDto);
        }


        [HttpPost]
        [Authorize]
        public async Task<ActionResult<ProductDto>> CreateProduct([FromBody] ProductDto productDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var category = await _categoryRepository.GetCategoryByIdAsync(productDto.CategoryId);
            if (category == null)
            {
                return BadRequest("Invalid category ID.");
            }

            var product = _mapper.Map<Product>(productDto);
            product.Id = Guid.NewGuid();

            await _productRepository.AddProductAsync(product);

            var createdProductDto = _mapper.Map<ProductDto>(product);
            return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, createdProductDto);
        }

        [HttpPut("{id}")]
        [Authorize]
        public async Task<ActionResult<ProductDto>> UpdateProduct(Guid id, [FromBody] ProductDto productDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var product = await _productRepository.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound();
            }

            var category = await _categoryRepository.GetCategoryByIdAsync(productDto.CategoryId);
            if (category == null)
            {
                return BadRequest("Invalid category ID.");
            }

            _mapper.Map(productDto, product);

            await _productRepository.UpdateProductAsync(product);

            var updatedProductDto = _mapper.Map<ProductDto>(product);
            return Ok(updatedProductDto);
        }

        [HttpGet("categories")]
        public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories()
        {
            var categories = await _categoryRepository.GetCategoriesAsync();
            var categoryDtos = _mapper.Map<IEnumerable<CategoryDto>>(categories);
            return Ok(categoryDtos);
        }

        [HttpPost("categories")]
        [Authorize]
        public async Task<ActionResult<CategoryDto>> CreateCategory([FromBody] CategoryDto categoryDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var category = new Category(Guid.NewGuid(), categoryDto.Name)
            {
                Name = categoryDto.Name
            };

            await _categoryRepository.AddCategoryAsync(category);

            var createdCategoryDto = _mapper.Map<CategoryDto>(category);
            return CreatedAtAction(nameof(GetCategories), new { id = category.Id }, createdCategoryDto);
        }

    }
    
    public class ProductDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public long Stock { get; set; }
        public Guid CategoryId { get; set; }
        public List<string>? ImageUrls { get; set; }
    }

    public class CategoryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
