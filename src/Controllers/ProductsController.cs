using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RitualWorks.Contracts;
using RitualWorks.Db;
using RitualWorks.Repositories;

namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;

        public ProductsController(IProductRepository productRepository, ICategoryRepository categoryRepository)
        {
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
        {
            var products = await _productRepository.GetProductsAsync();
            return Ok(products);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> GetProduct(Guid id)
        {
            var product = await _productRepository.GetProductByIdAsync(id);

            if (product == null)
            {
                return NotFound();
            }

            return Ok(product);
        }

        [HttpPost]
        public async Task<ActionResult<Product>> CreateProduct([FromBody] ProductDto productDto)
        {
            var category = await _categoryRepository.GetCategoryByIdAsync(productDto.CategoryId);
            if (category == null)
            {
                return BadRequest("Invalid category ID.");
            }

            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = productDto.Name,
                Description = productDto.Description,
                Price = productDto.Price,
                Stock = productDto.Stock,
                CategoryId = productDto.CategoryId,
                ProductImages = new List<ProductImage>()
            };

            foreach (var imageUrl in productDto.ImageUrls)
            {
                product.ProductImages.Add(new ProductImage { ImageUrl = imageUrl });
            }

            await _productRepository.AddProductAsync(product);

            return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
        }

        [HttpGet("categories")]
        public async Task<ActionResult<IEnumerable<Category>>> GetCategories()
        {
            var categories = await _categoryRepository.GetCategoriesAsync();
            return Ok(categories);
        }

        [HttpPost("categories")]
        public async Task<ActionResult<Category>> CreateCategory([FromBody] CategoryDto categoryDto)
        {
            var category = new Category
            {
                Id = Guid.NewGuid(),
                Name = categoryDto.Name
            };

            await _categoryRepository.AddCategoryAsync(category);

            return CreatedAtAction(nameof(GetCategories), new { id = category.Id }, category);
        }
    }

    public class ProductDto
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; } 
        public long Stock { get; set; }
        public Guid CategoryId { get; set; }
        public List<string>? ImageUrls { get; set; }
    }

    public class CategoryDto
    {
        public string Name { get; set; }
    }
}
