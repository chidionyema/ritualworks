using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using haworks.Controllers;
using haworks.Db;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using static haworks.Controllers.AuthenticationController;

namespace haworks.Tests
{
    [Collection("Integration Tests")]
    public class CheckoutControllerTests : IAsyncLifetime
    {
        private readonly HttpClient _client;
        private readonly IntegrationTestFixture _fixture;
        private readonly UserManager<User> _userManager;
        private readonly List<User> _testUsers = new();
        private readonly IConfiguration _configuration;
        private readonly WebApplicationFactory<Program> _factory;

        public CheckoutControllerTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _factory = _fixture.CreateFactory();
            _client = _factory.CreateClient();

            // Set up authentication header if needed
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");

            var scope = _factory.Services.CreateScope();
            _userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            _configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        }

        public async Task InitializeAsync()
        {
            // No initialization needed here
            await Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            // Cleanup test users
            foreach (var user in _testUsers)
            {
                await _userManager.DeleteAsync(user);
            }

            // Dispose of the factory
            _factory.Dispose();
        }

        [Fact]
        public async Task CreateCheckoutSession_ReturnsOk()
        {
            // Arrange
            var uniqueUsername = $"testuser_{Guid.NewGuid()}";
            var registrationDto = new UserRegistrationDto
            {
                Username = uniqueUsername,
                Email = $"{uniqueUsername}@example.com",
                Password = "Test@123"
            };

            // Register the user using the registration endpoint
            var registrationResponse = await _client.PostAsJsonAsync("/api/authentication/register", registrationDto);
            registrationResponse.EnsureSuccessStatusCode();

            var loginDto = new UserLoginDto
            {
                Username = uniqueUsername,
                Password = "Test@123"
            };

            var loginResponse = await _client.PostAsJsonAsync("/api/authentication/login", loginDto);
            loginResponse.EnsureSuccessStatusCode();
            var loginData = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
            var token = loginData.Token;

            // Set the Bearer token for authenticated requests
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Insert a test product directly
            var testProductId = await InsertTestProduct();

            var items = new List<CheckoutItem>
            {
                new CheckoutItem { ProductId = testProductId, Quantity = 1, Price = 9.99M, Name = "Test Product" }
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/checkout/create-session", items);

            // Assert
            response.EnsureSuccessStatusCode();
            var responseData = await response.Content.ReadFromJsonAsync<dynamic>();
            Assert.NotNull(responseData);
            Assert.NotNull(responseData.id);

            // Track the created user for cleanup
            var user = await _userManager.FindByNameAsync(registrationDto.Username);
            if (user != null) _testUsers.Add(user);
        }

        [Fact]
        public async Task CreateCheckoutSession_ReturnsBadRequest_WhenProductNotFound()
        {
            // Arrange
            var uniqueUsername = $"testuser_{Guid.NewGuid()}";
            var registrationDto = new UserRegistrationDto
            {
                Username = uniqueUsername,
                Email = $"{uniqueUsername}@example.com",
                Password = "Test@123"
            };

            var registrationResponse = await _client.PostAsJsonAsync("/api/authentication/register", registrationDto);
            registrationResponse.EnsureSuccessStatusCode();

            var loginDto = new UserLoginDto
            {
                Username = uniqueUsername,
                Password = "Test@123"
            };

            var loginResponse = await _client.PostAsJsonAsync("/api/authentication/login", loginDto);
            loginResponse.EnsureSuccessStatusCode();
            var loginData = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
            var token = loginData.Token;

            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var items = new List<CheckoutItem>
            {
                new CheckoutItem { ProductId = Guid.NewGuid(), Quantity = 1, Price = 9.99M, Name = "Non-existent Product" }
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/checkout/create-session", items);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

            // Track the created user for cleanup
            var user = await _userManager.FindByNameAsync(registrationDto.Username);
            if (user != null) _testUsers.Add(user);
        }

        [Fact]
        public async Task CreateCheckoutSession_ReturnsBadRequest_WhenInsufficientStock()
        {
            // Arrange
            var uniqueUsername = $"testuser_{Guid.NewGuid()}";
            var registrationDto = new UserRegistrationDto
            {
                Username = uniqueUsername,
                Email = $"{uniqueUsername}@example.com",
                Password = "Test@123"
            };

            var registrationResponse = await _client.PostAsJsonAsync("/api/authentication/register", registrationDto);
            registrationResponse.EnsureSuccessStatusCode();

            var loginDto = new UserLoginDto
            {
                Username = uniqueUsername,
                Password = "Test@123"
            };

            var loginResponse = await _client.PostAsJsonAsync("/api/authentication/login", loginDto);
            loginResponse.EnsureSuccessStatusCode();
            var loginData = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
            var token = loginData.Token;

            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Insert a test product directly
            var testProductId = await InsertTestProduct();

            var items = new List<CheckoutItem>
            {
                new CheckoutItem { ProductId = testProductId, Quantity = 100, Price = 9.99M, Name = "Test Product" } // Exceeds available stock
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/checkout/create-session", items);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

            // Track the created user for cleanup
            var user = await _userManager.FindByNameAsync(registrationDto.Username);
            if (user != null) _testUsers.Add(user);
        }

        private async Task<Guid> InsertTestProduct()
        {
            using var scope = _factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<haworksContext>();

            // Create and insert a test product
            var testProduct = new Product
            {
                Id = Guid.NewGuid(),
                Name = "Test Product",
                Description = "This is a test product.",
                Price = 9.99M,
                Stock = 10,
                CreatedDate = DateTime.UtcNow,
                Category = context.Categories.FirstOrDefault() ?? new Category { Id = Guid.NewGuid(), Name = "Test Category" }
            };

            context.Products.Add(testProduct);
            await context.SaveChangesAsync();

            return testProduct.Id;
        }
    }
}
