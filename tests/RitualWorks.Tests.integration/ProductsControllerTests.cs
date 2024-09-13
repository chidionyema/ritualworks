using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;
using System.Threading.Tasks;
using RitualWorks.Controllers;

namespace RitualWorks.Tests
{
    [Collection("Integration Tests")]
    public class ProductsControllerTests
    {
        private readonly HttpClient _client;

        public ProductsControllerTests(IntegrationTestFixture fixture)
        {
            _client = fixture.Client;
        }

        [Fact]
        public async Task GetProducts_ReturnsOk()
        {
            var response = await _client.GetAsync("/api/products");
            response.EnsureSuccessStatusCode();

            var products = await response.Content.ReadFromJsonAsync<IEnumerable<ProductDto>>();
            Assert.NotNull(products);
        }

        [Fact]
        public async Task CreateProduct_ReturnsCreated()
        {
            // Create a category first
            var categoryDto = new CategoryDto { Name = "Test Category" };
            var categoryResponse = await _client.PostAsJsonAsync("/api/products/categories", categoryDto);
            categoryResponse.EnsureSuccessStatusCode();
            var createdCategory = await categoryResponse.Content.ReadFromJsonAsync<CategoryDto>();

            var productDto = new ProductDto
            {
                Name = "Test Product",
                Description = "Test Description",
                Price = 9.99M,
                Stock = 10,
                CategoryId = createdCategory.Id,
                ImageUrls = new List<string> { "http://example.com/image.jpg" }
            };

            var response = await _client.PostAsJsonAsync("/api/products", productDto);
            response.EnsureSuccessStatusCode();
            var createdProduct = await response.Content.ReadFromJsonAsync<ProductDto>();
            Assert.NotNull(createdProduct);
            Assert.Equal(productDto.Name, createdProduct.Name);
        }

        [Fact]
        public async Task UpdateProduct_ReturnsOk()
        {
            // Create a category first
            var categoryDto = new CategoryDto { Name = "Test Category" };
            var categoryResponse = await _client.PostAsJsonAsync("/api/products/categories", categoryDto);
            categoryResponse.EnsureSuccessStatusCode();
            var createdCategory = await categoryResponse.Content.ReadFromJsonAsync<CategoryDto>();

            // First, create a product
            var createProductDto = new ProductDto
            {
                Name = "Test Product",
                Description = "Test Description",
                Price = 9.99M,
                Stock = 10,
                CategoryId = createdCategory.Id,
                ImageUrls = new List<string> { "http://example.com/image.jpg" }
            };
            var createResponse = await _client.PostAsJsonAsync("/api/products", createProductDto);
            var createdProduct = await createResponse.Content.ReadFromJsonAsync<ProductDto>();

            // Now update the product
            createdProduct.Name = "Updated Product Name";
            var updateResponse = await _client.PutAsJsonAsync($"/api/products/{createdProduct.Id}", createdProduct);
            updateResponse.EnsureSuccessStatusCode();

            // Verify the update
            var getResponse = await _client.GetAsync($"/api/products/{createdProduct.Id}");
            var updatedProduct = await getResponse.Content.ReadFromJsonAsync<ProductDto>();
            Assert.Equal("Updated Product Name", updatedProduct.Name);
        }

        [Fact]
        public async Task GetProductsByCategory_ReturnsOk()
        {
            // Create a category first
            var categoryDto = new CategoryDto { Name = "Test Category" };
            var categoryResponse = await _client.PostAsJsonAsync("/api/products/categories", categoryDto);
            categoryResponse.EnsureSuccessStatusCode();
            var createdCategory = await categoryResponse.Content.ReadFromJsonAsync<CategoryDto>();

            // Create a product in that category
            var productDto = new ProductDto
            {
                Name = "Test Product",
                Description = "Test Description",
                Price = 9.99M,
                Stock = 10,
                CategoryId = createdCategory.Id,
                ImageUrls = new List<string> { "http://example.com/image.jpg" }
            };

            var productResponse = await _client.PostAsJsonAsync("/api/products", productDto);
            productResponse.EnsureSuccessStatusCode();

            // Get products by category
            var response = await _client.GetAsync($"/api/products/categories/{createdCategory.Id}");
            response.EnsureSuccessStatusCode();
            var products = await response.Content.ReadFromJsonAsync<IEnumerable<ProductDto>>();

            Assert.NotEmpty(products);
            Assert.All(products, p => Assert.Equal(createdCategory.Id, p.CategoryId));
        }

        [Fact]
        public async Task CreateProduct_WithInvalidData_ReturnsBadRequest()
        {
            var invalidProductDto = new ProductDto
            {
                Name = "", // Invalid: empty name
                Price = -10, // Invalid: negative price
                CategoryId = Guid.Empty // Invalid: empty GUID
            };

            var response = await _client.PostAsJsonAsync("/api/products", invalidProductDto);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GetProduct_ReturnsNotFound_ForInvalidId()
        {
            var invalidId = Guid.NewGuid();
            var response = await _client.GetAsync($"/api/products/{invalidId}");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetProducts_WithPagination_ReturnsCorrectPage()
        {
            // Create a category first
            var categoryDto = new CategoryDto { Name = "Test Category" };
            var categoryResponse = await _client.PostAsJsonAsync("/api/products/categories", categoryDto);
            categoryResponse.EnsureSuccessStatusCode();
            var createdCategory = await categoryResponse.Content.ReadFromJsonAsync<CategoryDto>();

            // Create multiple products
            for (int i = 0; i < 20; i++)
            {
                var productDto = new ProductDto
                {
                    Name = $"Product {i:D2}", // Ensure names are formatted to maintain order
                    Description = "Test Description",
                    Price = 9.99M,
                    Stock = 10,
                    CategoryId = createdCategory.Id,
                    ImageUrls = new List<string> { "http://example.com/image.jpg" }
                };

                await _client.PostAsJsonAsync("/api/products", productDto);
            }

            // Get the second page of products (assuming 10 per page)
            var response = await _client.GetAsync("/api/products?page=2&pageSize=10");
            response.EnsureSuccessStatusCode();
            var products = await response.Content.ReadFromJsonAsync<IEnumerable<ProductDto>>();

            Assert.Equal(10, products.Count());
            // Ensure products are from the second page, i.e., Product 10 to Product 19
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal($"Product {i + 10:D2}", products.ElementAt(i).Name);
            }
        }





    }
}
