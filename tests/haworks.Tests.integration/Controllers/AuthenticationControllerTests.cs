using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using haworks.Controllers; // For DTO classes
using haworks.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Haworks.Tests
{
    [Collection("Integration Tests")]
    public class AuthenticationControllerWithDockerTests
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly HttpClient _client;
        private readonly WebApplicationFactory<Program> _factory;

        public AuthenticationControllerWithDockerTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _factory = _fixture.CreateFactory();
            _client = _fixture.CreateClientWithCookies();
        }

        // Helper: generate a unique suffix.
        private string GetUniqueSuffix() => Guid.NewGuid().ToString("N").Substring(0, 8);

        #region Register Integration Tests

        [Fact]
        public async Task Register_ValidInput_ReturnsOkAndCreatesUser_Docker()
        {
            await _fixture.ResetDatabaseAsync();
            var unique = GetUniqueSuffix();
            var registrationDto = new UserRegistrationDto 
            { 
                Username = "dockerTestNewUser_" + unique, 
                Email = $"dockernewuser_{unique}@example.com", 
                Password = "Password123!"
            };

            var response = await _client.PostAsJsonAsync("/api/Authentication/register", registrationDto);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var responseObject = await response.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.NotNull(responseObject!.RootElement.GetProperty("token").GetString());
            Assert.NotNull(responseObject.RootElement.GetProperty("userId").GetString());
            Assert.NotNull(responseObject.RootElement.GetProperty("expires").GetString());

            using (var scope = _factory.Services.CreateScope())
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
            await _fixture.ResetDatabaseAsync();
            var invalidRegistrationDto = new UserRegistrationDto 
            { 
                Username = null,
                Email = "invalid-email",
                Password = "short"
            };

            var response = await _client.PostAsJsonAsync("/api/Authentication/register", invalidRegistrationDto);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        #endregion

        #region Login Integration Tests

        [Fact]
        public async Task Login_ValidCredentials_ReturnsOkWithTokenUserAndCookie_Docker()
        {
            await _fixture.ResetDatabaseAsync();
            var unique = GetUniqueSuffix();
            var registrationDto = new UserRegistrationDto 
            { 
                Username = "testuser_" + unique, 
                Email = $"testuser_{unique}@example.com", 
                Password = "IntegrationTestPassword123!" 
            };
            var registerResponse = await _client.PostAsJsonAsync("/api/Authentication/register", registrationDto);
            registerResponse.EnsureSuccessStatusCode();

            var loginDto = new UserLoginDto 
            { 
                Username = registrationDto.Username, 
                Password = registrationDto.Password
            };

            var response = await _client.PostAsJsonAsync("/api/Authentication/login", loginDto);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var responseObject = await response.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.NotNull(responseObject!.RootElement.GetProperty("token").GetString());
            Assert.NotNull(responseObject.RootElement.GetProperty("user").GetProperty("id").GetString());
            Assert.NotNull(responseObject.RootElement.GetProperty("expires").GetString());
            Assert.False(responseObject.RootElement.GetProperty("user").GetProperty("isSubscribed").GetBoolean());

            var cookies = _fixture.CookieContainer.GetCookies(_client.BaseAddress);
            var jwtCookie = cookies.Cast<Cookie>().FirstOrDefault(c => c.Name.Equals("jwt", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(jwtCookie);
        }

        [Fact]
        public async Task Login_InvalidCredentials_ReturnsUnauthorized_Docker()
        {
            await _fixture.ResetDatabaseAsync();
            var loginDto = new UserLoginDto 
            { 
                Username = "nonexistent", 
                Password = "wrongPassword" 
            };

            var response = await _client.PostAsJsonAsync("/api/Authentication/login", loginDto);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        #endregion

        #region VerifyToken Integration Tests

        [Fact]
        public async Task VerifyToken_ValidTokenInHeader_ReturnsOkWithUserInfo_Docker()
        {
            await _fixture.ResetDatabaseAsync();
            var unique = GetUniqueSuffix();
            var registrationDto = new UserRegistrationDto 
            { 
                Username = "dockerVerifyTokenUser_" + unique, 
                Email = $"dockerverifytoken_{unique}@example.com", 
                Password = "Password123!" 
            };
            var registerResponse = await _client.PostAsJsonAsync("/api/Authentication/register", registrationDto);
            registerResponse.EnsureSuccessStatusCode();
            var registerResponseObject = await registerResponse.Content.ReadFromJsonAsync<JsonDocument>();
            var token = registerResponseObject!.RootElement.GetProperty("token").GetString();

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _client.GetAsync("/api/Authentication/verify-token");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var responseObject = await response.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.Equal(registrationDto.Username, responseObject!.RootElement.GetProperty("userName").GetString());
            Assert.NotNull(responseObject.RootElement.GetProperty("userId").GetString());
            Assert.False(responseObject.RootElement.GetProperty("isSubscribed").GetBoolean());
        }

        [Fact]
        public async Task VerifyToken_NoTokenInHeader_ReturnsUnauthorized_Docker()
        {
            await _fixture.ResetDatabaseAsync();
            _client.DefaultRequestHeaders.Authorization = null;
            var response = await _client.GetAsync("/api/Authentication/verify-token");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task VerifyToken_InvalidToken_ReturnsUnauthorized_Docker()
        {
            await _fixture.ResetDatabaseAsync();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.jwt.token");
            var response = await _client.GetAsync("/api/Authentication/verify-token");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        #endregion

        #region RefreshToken Integration Tests

        [Fact]
        public async Task RefreshToken_ValidTokens_ReturnsOkWithNewTokens_Docker()
        {
            await _fixture.ResetDatabaseAsync();
            var unique = GetUniqueSuffix();
            var registrationDto = new UserRegistrationDto 
            { 
                Username = "dockerRefreshTokenUser_" + unique, 
                Email = $"dockerrefreshtoken_{unique}@example.com", 
                Password = "Password123!" 
            };
            var registerResponse = await _client.PostAsJsonAsync("/api/Authentication/register", registrationDto);
            registerResponse.EnsureSuccessStatusCode();
            var registerResponseObject = await registerResponse.Content.ReadFromJsonAsync<JsonDocument>();
            var accessToken = registerResponseObject!.RootElement.GetProperty("token").GetString();
            var userId = registerResponseObject.RootElement.GetProperty("userId").GetString();
            var refreshTokenString = "testRefreshTokenValueDocker";

            // Clear any "jwt" cookie so it does not override our Authorization header.
            var cookies = _fixture.CookieContainer.GetCookies(_client.BaseAddress).Cast<Cookie>().ToList();
            foreach (var cookie in cookies.Where(c => c.Name.Equals("jwt", StringComparison.OrdinalIgnoreCase)))
            {
                cookie.Expired = true;
            }

            // Manually insert a refresh token.
            using (var scope = _factory.Services.CreateScope())
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

            var refreshTokenRequest = new RefreshTokenRequest 
            { 
                AccessToken = accessToken, 
                RefreshToken = refreshTokenString 
            };

            var response = await _client.PostAsJsonAsync("/api/Authentication/refresh-token", refreshTokenRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var responseObject = await response.Content.ReadFromJsonAsync<JsonDocument>();
            Assert.NotNull(responseObject!.RootElement.GetProperty("accessToken").GetString());
            Assert.NotNull(responseObject.RootElement.GetProperty("refreshToken").GetString());
            Assert.NotNull(responseObject.RootElement.GetProperty("expires").GetString());

            using (var scope = _factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<haworksContext>();
                var oldRefreshToken = await context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshTokenString);
                Assert.Null(oldRefreshToken);
            }
        }

        [Fact]
        public async Task RefreshToken_InvalidAccessToken_ReturnsUnauthorized_Docker()
        {
            await _fixture.ResetDatabaseAsync();
            var refreshTokenRequest = new RefreshTokenRequest 
            { 
                AccessToken = "invalid.access.token", 
                RefreshToken = "someRefreshToken" 
            };

            var response = await _client.PostAsJsonAsync("/api/Authentication/refresh-token", refreshTokenRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task RefreshToken_InvalidRefreshToken_ReturnsUnauthorized_Docker()
        {
            await _fixture.ResetDatabaseAsync();
            var unique = GetUniqueSuffix();
            var registrationDto = new UserRegistrationDto 
            { 
                Username = "dockerInvalidRefreshTokenUser_" + unique, 
                Email = $"dockerinvalidrefreshtoken_{unique}@example.com", 
                Password = "Password123!" 
            };
            var registerResponse = await _client.PostAsJsonAsync("/api/Authentication/register", registrationDto);
            registerResponse.EnsureSuccessStatusCode();
            var registerResponseObject = await registerResponse.Content.ReadFromJsonAsync<JsonDocument>();
            var accessToken = registerResponseObject!.RootElement.GetProperty("token").GetString();

            var refreshTokenRequest = new RefreshTokenRequest 
            { 
                AccessToken = accessToken, 
                RefreshToken = "invalidRefreshToken" 
            };

            var response = await _client.PostAsJsonAsync("/api/Authentication/refresh-token", refreshTokenRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        #endregion

        #region Logout Integration Tests

        [Fact]
        public async Task Logout_ReturnsOkAndDeletesCookie_Docker()
        {
            await _fixture.ResetDatabaseAsync();
            var unique = GetUniqueSuffix();
            var registrationDto = new UserRegistrationDto 
            { 
                Username = "testuser_" + unique, 
                Email = $"testuser_{unique}@example.com", 
                Password = "IntegrationTestPassword123!" 
            };
            var registerResponse = await _client.PostAsJsonAsync("/api/Authentication/register", registrationDto);
            registerResponse.EnsureSuccessStatusCode();

            var loginDto = new UserLoginDto 
            { 
                Username = registrationDto.Username, 
                Password = registrationDto.Password
            };
            var loginResponse = await _client.PostAsJsonAsync("/api/Authentication/login", loginDto);
            loginResponse.EnsureSuccessStatusCode();

            var cookiesBefore = _fixture.CookieContainer.GetCookies(_client.BaseAddress);
            var jwtCookieBefore = cookiesBefore.Cast<Cookie>().FirstOrDefault(c => c.Name.Equals("jwt", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(jwtCookieBefore);

            var logoutResponse = await _client.PostAsync("/api/Authentication/logout", null);
            Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

            var cookiesAfter = _fixture.CookieContainer.GetCookies(_client.BaseAddress);
            var jwtCookieAfter = cookiesAfter.Cast<Cookie>().FirstOrDefault(c => c.Name.Equals("jwt", StringComparison.OrdinalIgnoreCase));
            Assert.Null(jwtCookieAfter);
        }

        #endregion

        #region External Authentication Endpoints

        [Fact]
        public async Task ExternalMicrosoft_ReturnsChallenge_Docker()
        {
            await _fixture.ResetDatabaseAsync();
            var response = await _client.GetAsync("/api/Authentication/external/microsoft");
            Assert.True(response.StatusCode == HttpStatusCode.Redirect ||
                        response.StatusCode == HttpStatusCode.Unauthorized ||
                        response.StatusCode == HttpStatusCode.OK);
        }

        #endregion
    }
}
