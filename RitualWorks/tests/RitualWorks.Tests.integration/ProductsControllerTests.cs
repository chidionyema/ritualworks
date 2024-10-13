using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RitualWorks.Controllers;
using RitualWorks.Db;
using Xunit;

namespace RitualWorks.Tests
{
    [Collection("Integration Tests")]
    public class ProductsControllerTests : IAsyncLifetime
    {
        private readonly HttpClient _client;
        private readonly IntegrationTestFixture _fixture;
        private readonly RitualWorksContext _context;

        public ProductsControllerTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            var factory = fixture.CreateFactory();
            _client = factory.CreateClient();

            // Set up authentication header if needed
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");

            // Access the DbContext for cleanup
            _context = factory.Services.CreateScope().ServiceProvider.GetRequiredService<RitualWorksContext>();
        }

        public async Task InitializeAsync()
        {
            // Clean up data before each test
            await ClearDatabaseAsync();
        }

        public async Task DisposeAsync()
        {
            // Clean up data after each test
            await ClearDatabaseAsync();
        }

        private async Task ClearDatabaseAsync()
        {
            // Clear products, categories, and other related entities
            _context.Products.RemoveRange(_context.Products);
            _context.Categories.RemoveRange(_context.Categories);
            // Add more entities as necessary
            await _context.SaveChangesAsync();
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
            createResponse.EnsureSuccessStatusCode();
            var createdProduct = await createResponse.Content.ReadFromJsonAsync<ProductDto>();

            // Now update the product
            createdProduct.Name = "Updated Product Name";
            var updateResponse = await _client.PutAsJsonAsync($"/api/products/{createdProduct.Id}", createdProduct);
            updateResponse.EnsureSuccessStatusCode();

            // Verify the update
            var getResponse = await _client.GetAsync($"/api/products/{createdProduct.Id}");
            getResponse.EnsureSuccessStatusCode();
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

            // Create multiple products with alphabetic names to ensure predictable ordering
            var productNames = new List<string>
            {
                "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliet",
                "Kilo", "Lima", "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango"
            };

            foreach (var name in productNames)
            {
                var productDto = new ProductDto
                {
                    Name = name,
                    Description = "Test Description",
                    Price = 9.99M,
                    Stock = 10,
                    CategoryId = createdCategory.Id,
                    ImageUrls = new List<string> { "http://example.com/image.jpg" }
                };

                await _client.PostAsJsonAsync("/api/products", productDto);
            }

            // Retrieve the second page of products, expecting items sorted alphabetically
            var response = await _client.GetAsync("/api/products?page=2&pageSize=10");
            response.EnsureSuccessStatusCode();
            var products = await response.Content.ReadFromJsonAsync<IEnumerable<ProductDto>>();

            // Assert the page contains the correct number of items
            Assert.Equal(10, products.Count());

            // Manually define the expected names based on alphabetical order of the full list
            var expectedNames = new List<string>
            {
                "Kilo", "Lima", "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango"
            };

            var actualNames = products.Select(p => p.Name).ToList();

            // Compare the actual and expected results
            Assert.Equal(expectedNames, actualNames);
        }
    }
}
