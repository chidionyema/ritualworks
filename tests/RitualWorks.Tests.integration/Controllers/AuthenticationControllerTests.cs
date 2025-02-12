using Xunit;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http;
using haworks.Db;
using static haworks.Controllers.AuthenticationController;
using System.Threading.Tasks;
using Microsoft.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection; // **ADD THIS LINE - For CreateScope()**

namespace haworks.Tests.integration
{
    [Collection("Integration Tests")] // Use the collection definition from your fixture
    public class AuthenticationControllerWithDockerTests
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly HttpClient _client;
        private readonly WebApplicationFactory<Program> _factory; // Add factory

        public AuthenticationControllerWithDockerTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _factory = fixture.CreateFactory(); // Create WebApplicationFactory from fixture
            _client = _factory.CreateClient(); // Use the client from the factory
        }

        #region Register Integration Tests with Docker

        [Fact]
        public async Task Register_ValidInput_ReturnsOkAndCreatesUser_Docker()
        {
            // Arrange
            await _fixture.ResetDatabaseAsync(); // Reset database using fixture method
            var registrationDto = new UserRegistrationDto { Username = "dockerTestNewUser", Email = "dockernewuser@example.com", Password = "Password123!" };

            // Act
            var response = await _client.PostAsJsonAsync("/api/Authentication/register", registrationDto); // Use _client

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseObject = await response.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.NotNull(responseObject!.RootElement.GetProperty("Token").GetString());
            Assert.NotNull(responseObject!.RootElement.GetProperty("UserId").GetString());
            Assert.NotNull(responseObject!.RootElement.GetProperty("Expires").GetString());

            // Verify user created in database (using _factory.Services for scope)
            using (var scope = _factory.Services.CreateScope()) // Use _factory.Services
            {
                var context = scope.ServiceProvider.GetRequiredService<haworksContext>();
                var user = await context.Users.FirstOrDefaultAsync(u => u.UserName == registrationDto.Username);
                Assert.NotNull(user);
                Assert.Equal(registrationDto.Email, user.Email);
            }
        }


        [Fact]
        public async Task Register_InvalidInput_ReturnsBadRequest_Docker()
        {
            // Arrange
            await _fixture.ResetDatabaseAsync();
            var invalidRegistrationDto = new UserRegistrationDto { Username = null, Email = "invalid-email", Password = "short" }; // Invalid DTO

            // Act
            var response = await _client.PostAsJsonAsync("/api/Authentication/register", invalidRegistrationDto); // Use _client

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            // You can further assert the response body if you want to check specific validation errors
        }

        // Add more Register integration tests with Docker

        #endregion

        #region Login Integration Tests with Docker

        [Fact]
        public async Task Login_ValidCredentials_ReturnsOkWithTokenUserAndCookie_Docker()
        {
            // Arrange
            await _fixture.ResetDatabaseAsync();
            var loginDto = new UserLoginDto { Username = "testuser", Password = "IntegrationTestPassword123!" }; // Use seeded test user from fixture

            // Act
            var response = await _client.PostAsJsonAsync("/api/Authentication/login", loginDto); // Use _client

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseObject = await response.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.NotNull(responseObject!.RootElement.GetProperty("Token").GetString());
            Assert.NotNull(responseObject!.RootElement.GetProperty("User").GetProperty("Id").GetString());
            Assert.NotNull(responseObject!.RootElement.GetProperty("Expires").GetString());
            Assert.True(responseObject!.RootElement.GetProperty("User").GetProperty("isSubscribed").GetBoolean());

            // Check JWT cookie is set in response headers
            Assert.NotNull(response.Headers.GetCookies().FirstOrDefault(cookie => cookie.Name == "jwt"));
        }

        [Fact]
        public async Task Login_InvalidCredentials_ReturnsUnauthorized_Docker()
        {
            // Arrange
            await _fixture.ResetDatabaseAsync();
            var loginDto = new UserLoginDto { Username = "testuser", Password = "wrongPassword" };

            // Act
            var response = await _client.PostAsJsonAsync("/api/Authentication/login", loginDto); // Use _client

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            // Optionally assert response body for error message
        }

        // Add more Login integration tests with Docker

        #endregion

        #region VerifyToken Integration Tests with Docker

        [Fact]
        public async Task VerifyToken_ValidTokenInHeader_ReturnsOkWithUserInfo_Docker()
        {
            // Arrange
            await _fixture.ResetDatabaseAsync();
            // 1. Register and Login to get a token
            var registrationDto = new UserRegistrationDto { Username = "dockerVerifyTokenUser", Email = "dockerverifytoken@example.com", Password = "Password123!" };
            var registerResponse = await _client.PostAsJsonAsync("/api/Authentication/register", registrationDto);
            registerResponse.EnsureSuccessStatusCode();
            var registerResponseObject = await registerResponse.Content.ReadFromJsonAsync<JsonDocument>();
            var token = registerResponseObject!.RootElement.GetProperty("Token").GetString();

            // 2. Set Authorization header for VerifyToken request
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Act
            var response = await _client.GetAsync("/api/Authentication/verify-token"); // Use _client

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseObject = await response.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.Equal(registrationDto.Username, responseObject!.RootElement.GetProperty("userName").GetString());
            Assert.NotNull(responseObject!.RootElement.GetProperty("userId").GetString());
            Assert.False(responseObject!.RootElement.GetProperty("isSubscribed").GetBoolean());
        }

        [Fact]
        public async Task VerifyToken_NoTokenInHeader_ReturnsUnauthorized_Docker()
        {
            // Arrange
            await _fixture.ResetDatabaseAsync();
            _client.DefaultRequestHeaders.Authorization = null; // Clear authorization header // Use _client

            // Act
            var response = await _client.GetAsync("/api/Authentication/verify-token"); // Use _client

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            // Optionally assert response body for error message
        }

        [Fact]
        public async Task VerifyToken_InvalidToken_ReturnsUnauthorized_Docker()
        {
            // Arrange
            await _fixture.ResetDatabaseAsync();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid.jwt.token"); // Use _client

            // Act
            var response = await _client.GetAsync("/api/Authentication/verify-token"); // Use _client

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
             // Optionally assert response body for error message
        }

        #endregion

        #region RefreshToken Integration Tests with Docker

        [Fact]
        public async Task RefreshToken_ValidTokens_ReturnsOkWithNewTokens_Docker()
        {
            // Arrange
            await _fixture.ResetDatabaseAsync();
            // 1. Register and Login to get initial tokens
            var registrationDto = new UserRegistrationDto { Username = "dockerRefreshTokenUser", Email = "dockerrefreshtoken@example.com", Password = "Password123!" };
            var registerResponse = await _client.PostAsJsonAsync("/api/Authentication/register", registrationDto);
            registerResponse.EnsureSuccessStatusCode();
            var registerResponseObject = await registerResponse.Content.ReadFromJsonAsync<JsonDocument>();
            var accessToken = registerResponseObject!.RootElement.GetProperty("Token").GetString();
            var refreshTokenString = "testRefreshTokenValueDocker"; // Unique refresh token for docker test
            var userId = registerResponseObject!.RootElement.GetProperty("UserId").GetString();


            // 2. Seed a valid RefreshToken entity in the database (using _factory.Services for scope)
            using (var scope = _factory.Services.CreateScope()) // Use _factory.Services
            {
                var context = scope.ServiceProvider.GetRequiredService<haworksContext>();
                var refreshTokenEntity = new RefreshToken
                {
                    UserId = userId,
                    Token = refreshTokenString,
                    Expires = DateTime.UtcNow.AddDays(1),
                    Created = DateTime.UtcNow
                };
                context.RefreshTokens.Add(refreshTokenEntity);
                await context.SaveChangesAsync();
            }

            // 3. Create RefreshTokenRequest
            var refreshTokenRequest = new RefreshTokenRequest { AccessToken = accessToken, RefreshToken = refreshTokenString };

            // Act
            var response = await _client.PostAsJsonAsync("/api/Authentication/refresh-token", refreshTokenRequest); // Use _client


            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseObject = await response.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.NotNull(responseObject!.RootElement.GetProperty("accessToken").GetString());
            Assert.NotNull(responseObject!.RootElement.GetProperty("refreshToken").GetString());
            Assert.NotNull(responseObject!.RootElement.GetProperty("expires").GetString());

            // Verify the old refresh token is removed (optional) (using _factory.Services for scope)
            using (var scope = _factory.Services.CreateScope()) // Use _factory.Services
            {
                var context = scope.ServiceProvider.GetRequiredService<haworksContext>();
                var oldRefreshToken = await context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshTokenString);
                Assert.Null(oldRefreshToken); // Should be removed after refresh
            }
        }


        [Fact]
        public async Task RefreshToken_InvalidAccessToken_ReturnsUnauthorized_Docker()
        {
            // Arrange
            await _fixture.ResetDatabaseAsync();
            var refreshTokenRequest = new Controllers.RefreshTokenRequest { AccessToken = "invalid.access.token", RefreshToken = "refreshToken" };

            // Act
            var response = await _client.PostAsJsonAsync("/api/Authentication/refresh-token", refreshTokenRequest); // Use _client

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
             // Optionally assert response body for error message
        }


        [Fact]
        public async Task RefreshToken_InvalidRefreshToken_ReturnsUnauthorized_Docker()
        {
            // Arrange
            await _fixture.ResetDatabaseAsync();
            // 1. Register and Login to get initial access token
            var registrationDto = new UserRegistrationDto { Username = "dockerInvalidRefreshTokenUser", Email = "dockerinvalidrefreshtoken@example.com", Password = "Password123!" };
            var registerResponse = await _client.PostAsJsonAsync("/api/Authentication/register", registrationDto);
            registerResponse.EnsureSuccessStatusCode();
            var registerResponseObject = await registerResponse.Content.ReadFromJsonAsync<JsonDocument>();
            var accessToken = registerResponseObject!.RootElement.GetProperty("Token").GetString();

            var refreshTokenRequest = new RefreshTokenRequest { AccessToken = accessToken, RefreshToken = "invalidRefreshToken" }; // Invalid refresh token

            // Act
            var response = await _client.PostAsJsonAsync("/api/Authentication/refresh-token", refreshTokenRequest); // Use _client

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
             // Optionally assert response body for error message
        }


        #endregion

        #region Logout Integration Tests with Docker

        [Fact]
        public async Task Logout_ReturnsOkAndDeletesCookie_Docker()
        {
            // Arrange
            await _fixture.ResetDatabaseAsync();
            // 1. Login to set a cookie
            var loginDto = new UserLoginDto { Username = "testuser", Password = "IntegrationTestPassword123!" };
            var loginResponse = await _client.PostAsJsonAsync("/api/Authentication/login", loginDto); // Use _client
            loginResponse.EnsureSuccessStatusCode();
            Assert.NotNull(loginResponse.Headers.GetCookies().FirstOrDefault(cookie => cookie.Name == "jwt")); // Verify cookie is initially set

            // Act
            var logoutResponse = await _client.PostAsync("/api/Authentication/logout", null); // Logout endpoint is POST // Use _client

            // Assert
            Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
            Assert.Null(logoutResponse.Headers.GetCookies().FirstOrDefault(cookie => cookie.Name == "jwt")); // Cookie should be deleted after logout
        }

        #endregion
    }
}