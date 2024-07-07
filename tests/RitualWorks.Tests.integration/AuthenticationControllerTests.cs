using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RitualWorks.Controllers;
using RitualWorks.Db;
using Xunit;
using static RitualWorks.Controllers.AuthenticationController;

namespace RitualWorks.Tests
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

        public AuthenticationControllerTests(IntegrationTestFixture fixture)
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
            Assert.Equal("User registered successfully", responseData);

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

            // Act
            var response = await _client.PostAsJsonAsync("/api/authentication/login", loginDto);

            // Assert
            response.EnsureSuccessStatusCode();
            var responseData = await response.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(responseData);
            Assert.NotNull(responseData.Token);
            Assert.NotNull(responseData.Expiration);

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

public class AuthResponse
{
    public string Token { get; set; }
    public DateTime Expiration { get; set; }
}
