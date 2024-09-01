using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RitualWorks.Controllers;
using RitualWorks.Db;
using Xunit;
using Microsoft.Extensions.Configuration;
using static RitualWorks.Controllers.AuthenticationController;
using RitualWorks.Contracts;

namespace RitualWorks.Tests
{
    [Collection("Integration Tests")]
    public class CheckoutControllerTests : IAsyncLifetime
    {
        private readonly HttpClient _client;
        private readonly IntegrationTestFixture _fixture;
        private readonly UserManager<User> _userManager;
        private readonly List<User> _testUsers = new();
        private readonly IConfiguration _configuration;

        public CheckoutControllerTests(IntegrationTestFixture fixture)
        {
            _client = fixture.Client;
            _fixture = fixture;
            var scope = _fixture.Factory.Services.CreateScope();
            _userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            _configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        }

        public async Task InitializeAsync()
        {
            // No need to initialize anything here
        }

        public async Task DisposeAsync()
        {
            // Cleanup test users
            foreach (var user in _testUsers)
            {
                await _userManager.DeleteAsync(user);
            }
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
            if (!registrationResponse.IsSuccessStatusCode)
            {
                var error = await registrationResponse.Content.ReadAsStringAsync();
                throw new Exception($"Registration failed: {error}");
            }

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
                new CheckoutItem { ProductId = GetTestProductId(), Quantity = 1, Price = 9.99M, Name = "Test Product" }
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

            // Register the user using the registration endpoint
            var registrationResponse = await _client.PostAsJsonAsync("/api/authentication/register", registrationDto);
            if (!registrationResponse.IsSuccessStatusCode)
            {
                var error = await registrationResponse.Content.ReadAsStringAsync();
                throw new Exception($"Registration failed: {error}");
            }

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

            // Register the user using the registration endpoint
            var registrationResponse = await _client.PostAsJsonAsync("/api/authentication/register", registrationDto);
            if (!registrationResponse.IsSuccessStatusCode)
            {
                var error = await registrationResponse.Content.ReadAsStringAsync();
                throw new Exception($"Registration failed: {error}");
            }

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
                new CheckoutItem { ProductId = GetTestProductId(), Quantity = 100, Price = 9.99M, Name = "Test Product" }
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/checkout/create-session", items);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

            // Track the created user for cleanup
            var user = await _userManager.FindByNameAsync(registrationDto.Username);
            if (user != null) _testUsers.Add(user);
        }

        private Guid GetTestProductId()
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var productRepository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
            var product = productRepository.GetProductsAsync(1, 1).Result.FirstOrDefault();
            return product?.Id ?? throw new Exception("Test product not found");
        }
    }
}
