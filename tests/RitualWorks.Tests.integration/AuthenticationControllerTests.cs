using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using haworks.Db;
using System.Text.Json;
using Xunit;
using haworks.Contracts;
using haworks.Controllers;
using static haworks.Controllers.AuthenticationController;
using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;

namespace haworks.Tests
{
    public class AuthResponse
    {
        public string Token { get; set; }
        public DateTime Expiration { get; set; }
    }

    [Collection("Integration Tests")]
    public class AuthenticationControllerTests : IAsyncLifetime
    {
        private readonly HttpClient _client;
        private readonly IntegrationTestFixture _fixture;
        private readonly UserManager<User> _userManager;
        private readonly List<User> _testUsers = new();
        private readonly IConfiguration _configuration;
        private readonly WebApplicationFactory<Program> _factory;

        public AuthenticationControllerTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _factory = _fixture.CreateFactory();
            _client = _factory.CreateClient();

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
        public async Task Register_ReturnsOk()
        {
            // Arrange
            var uniqueUsername = $"newuser_{Guid.NewGuid()}";
            var registrationDto = new UserRegistrationDto
            {
                Username = uniqueUsername,
                Email = $"{uniqueUsername}@example.com",
                Password = "Password@123"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/authentication/register", registrationDto);

            // Assert
            response.EnsureSuccessStatusCode();
            var responseData = await response.Content.ReadAsStringAsync();

            // Sanitize and extract the message
            using var jsonDoc = JsonDocument.Parse(responseData);
            var message = jsonDoc.RootElement.GetProperty("message").GetString()?.Trim();

            // Use Contains to avoid issues with exact formatting differences
            message.Should().Contain("User registered successfully");

            // Track the created user for cleanup
            var user = await _userManager.FindByNameAsync(registrationDto.Username);
            if (user != null) _testUsers.Add(user);
        }


        [Fact]
        public async Task Register_ReturnsBadRequest_WhenInvalid()
        {
            // Arrange
            var uniqueUsername = $"newuser_{Guid.NewGuid()}";
            var registrationDto = new UserRegistrationDto
            {
                Username = uniqueUsername,
                Email = $"{uniqueUsername}@example.com"
                // Missing Password
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/authentication/register", registrationDto);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Login_ReturnsOk()
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

            // Act
            var response = await _client.PostAsJsonAsync("/api/authentication/login", loginDto);

            // Assert
            response.EnsureSuccessStatusCode();
            var responseData = await response.Content.ReadFromJsonAsync<AuthResponse>();

            Assert.NotNull(responseData);

            // Track the created user for cleanup
            var user = await _userManager.FindByNameAsync(registrationDto.Username);
            if (user != null) _testUsers.Add(user);
        }

        [Fact]
        public async Task Login_ReturnsUnauthorized_WhenInvalid()
        {
            // Arrange
            var uniqueUsername = $"testuser_{Guid.NewGuid()}";
            var testUser = new User { UserName = uniqueUsername, Email = $"{uniqueUsername}@example.com", EmailConfirmed = true };
            var password = "Test@123";
            var creationResult = await _userManager.CreateAsync(testUser, password);

            if (!creationResult.Succeeded)
            {
                throw new Exception("Failed to create test user: " + string.Join(", ", creationResult.Errors));
            }

            _testUsers.Add(testUser);

            var loginDto = new UserLoginDto
            {
                Username = uniqueUsername,
                Password = "WrongPassword"
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/authentication/login", loginDto);

            // Assert
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
