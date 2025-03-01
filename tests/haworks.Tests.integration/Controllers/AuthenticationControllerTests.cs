using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;
using haworks.Controllers;
using haworks.Db;
using haworks.Dto;
using haworks.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Haworks.Infrastructure.Data;

namespace Haworks.Tests.Integration
{
    [Collection("Integration Tests")]
    public class AuthenticationControllerIntegrationTests : IAsyncLifetime
    {
        private readonly IntegrationTestFixture _fixture;

        // (A) The original client that shares the CookieContainer
        private readonly HttpClient _client; 
        
        // (B) A second client with NO cookies, for Bearer-based tests
        private readonly HttpClient _clientNoCookies;

        public AuthenticationControllerIntegrationTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClientWithCookies();
            // Create a second client without a cookie container:
            _clientNoCookies = fixture.Factory.CreateClient();
        }

        // Runs before each test class execution
        public async Task InitializeAsync()
        {
            await _fixture.ResetDatabaseAsync();
        }

        // Runs after each test class execution
        public Task DisposeAsync() => Task.CompletedTask;

        // ------------------------------------------------------
        // SECTION 1: Original Cookie-based Tests
        // ------------------------------------------------------

        #region Registration Tests (Cookie-based)

        [Fact(DisplayName = "[Cookie] POST /register - Success (Valid Model)")]
        public async Task Register_ValidModel_ReturnsOk()
        {
            // Arrange
            var registrationDto = new UserRegistrationDto
            {
                Username = $"reg_success_{Guid.NewGuid()}",
                Email = "testuser@example.com",
                Password = "Password123!"
            };

            // Act
            var response = await _client.PostAsJsonAsync("api/authentication/register", registrationDto);
            var responseBody = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Validate JSON fields
            var json = JObject.Parse(responseBody);
            Assert.True(json.ContainsKey("token"), "Response should contain 'token'");
            Assert.True(json.ContainsKey("userId"), "Response should contain 'userId'");
            Assert.True(json.ContainsKey("expires"), "Response should contain 'expires'");
        }

        [Fact(DisplayName = "[Cookie] POST /register - Failure (Invalid Model)")]
        public async Task Register_InvalidModel_ReturnsBadRequest()
        {
            // Arrange: Missing email, invalid password
            var registrationDto = new UserRegistrationDto
            {
                Username = "invalid_user",
                Email = "not-an-email", 
                Password = "123"  // fails MinLength=8
            };

            // Act
            var response = await _client.PostAsJsonAsync("api/authentication/register", registrationDto);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact(DisplayName = "[Cookie] POST /register - Failure (Duplicate Username)")]
        public async Task Register_DuplicateUsername_ReturnsBadRequest()
        {
            // Arrange
            var username = $"dup_user_{Guid.NewGuid()}";
            var firstDto = new UserRegistrationDto
            {
                Username = username,
                Email = $"{username}@example.com",
                Password = "Password123!"
            };
            var secondDto = new UserRegistrationDto
            {
                Username = username, // same username
                Email = $"another_{username}@example.com",
                Password = "AnotherPass123!"
            };

            // Act: First registration is successful
            var firstResponse = await _client.PostAsJsonAsync("api/authentication/register", firstDto);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            // Second registration with the same username should fail
            var secondResponse = await _client.PostAsJsonAsync("api/authentication/register", secondDto);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
        }

        #endregion

        #region Login Tests (Cookie-based)

        [Fact(DisplayName = "[Cookie] POST /login - Success (Valid Credentials)")]
        public async Task Login_ValidCredentials_ReturnsOk()
        {
            // Arrange: Register the user first
            var username = $"login_success_{Guid.NewGuid()}";
            var password = "Password123!";
            await RegisterTestUserCookie(username, $"{username}@example.com", password);

            var loginDto = new UserLoginDto
            {
                Username = username,
                Password = password
            };

            // Act
            var response = await _client.PostAsJsonAsync("api/authentication/login", loginDto);
            var responseBody = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = JObject.Parse(responseBody);
            Assert.True(json.ContainsKey("token"), "Response should contain 'token'.");
            Assert.True(json.ContainsKey("user"), "Response should contain 'user'.");
            Assert.True(json.ContainsKey("expires"), "Response should contain 'expires'.");

            // Additional check on user object
            var userObj = json["user"] as JObject;
            Assert.Equal(username, userObj?["userName"]?.ToString());
        }

        [Fact(DisplayName = "[Cookie] POST /login - Failure (Invalid Credentials)")]
        public async Task Login_InvalidCredentials_ReturnsUnauthorized()
        {
            // Arrange: This user doesn't exist
            var loginDto = new UserLoginDto
            {
                Username = "non_existent",
                Password = "WrongPassword"
            };

            // Act
            var response = await _client.PostAsJsonAsync("api/authentication/login", loginDto);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact(DisplayName = "[Cookie] POST /login - Failure (Invalid Model => BadRequest)")]
        public async Task Login_InvalidModel_ReturnsBadRequest()
        {
            // Arrange: Missing password
            var loginDto = new UserLoginDto
            {
                Username = "some_user"
            };

            // Act
            var response = await _client.PostAsJsonAsync("api/authentication/login", loginDto);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        #endregion

        #region Logout Tests (Cookie-based)

        [Fact(DisplayName = "[Cookie] POST /logout - Success (Cookie Cleared)")]
        public async Task Logout_Success_ClearsJwtCookie()
        {
            // Arrange: Register & Login to set cookie
            var username = $"logout_{Guid.NewGuid()}";
            var password = "Password123!";
            await RegisterTestUserCookie(username, $"{username}@example.com", password);
            await LoginTestUserCookie(username, password);

            // Act
            var response = await _client.PostAsync("api/authentication/logout", null);
            var responseBody = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Logged out successfully", responseBody);

            // Ensure the "jwt" cookie is deleted
            var cookies = _fixture.CookieContainer.GetCookies(_client.BaseAddress!);
            Assert.False(cookies.Cast<System.Net.Cookie>().Any(c => c.Name == "jwt"),
                         "JWT cookie should be removed after logout.");
        }

        #endregion

        #region Verify-Token Tests (Cookie-based)

        [Fact(DisplayName = "[Cookie] GET /verify-token - Success (Valid Cookie)")]
        public async Task VerifyToken_ValidCookie_ReturnsOk()
        {
            // Arrange: Register & Login
            var username = $"verify_{Guid.NewGuid()}";
            var password = "Password123!";
            var userId = await RegisterTestUserCookie(username, $"{username}@example.com", password);
            await LoginTestUserCookie(username, password);

            // Act
            var response = await _client.GetAsync("api/authentication/verify-token");
            var responseBody = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = JObject.Parse(responseBody);
            Assert.Equal(userId, json["userId"]?.ToString());
            Assert.Equal(username, json["userName"]?.ToString());
        }

        [Fact(DisplayName = "[Cookie] GET /verify-token - Failure (No Cookie => Unauthorized)")]
        public async Task VerifyToken_NoCookie_ReturnsUnauthorized()
        {
            // Arrange: brand new client, no cookies
            var cleanClient = _fixture.Factory.CreateClient();

            // Act
            var response = await cleanClient.GetAsync("api/authentication/verify-token");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        #endregion

        #region Refresh-Token Tests (Cookie-based)

        [Fact(DisplayName = "[Cookie] POST /refresh-token - Success (Valid Access/Refresh Tokens)")]
        public async Task RefreshToken_ValidTokens_ReturnsNewTokens()
        {
            // Arrange: register user and login once to get the valid JWT
            var username = $"refresh_{Guid.NewGuid()}";
            var password = "Password123!";
            var userId = await RegisterTestUserCookie(username, $"{username}@example.com", password);

            // Perform a single login call and capture the access token from the response.
            var loginDto = new UserLoginDto 
            { 
                Username = username, 
                Password = password 
            };
            var loginResponse = await _client.PostAsJsonAsync("api/authentication/login", loginDto);
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

            var loginJson = JObject.Parse(await loginResponse.Content.ReadAsStringAsync());
            var oldAccessToken = loginJson["token"]?.ToString();
            Assert.False(string.IsNullOrEmpty(oldAccessToken), "Access token should not be null or empty.");

            // Clear the cookie so that it does not interfere with the refresh endpoint.
            var cookies = _fixture.CookieContainer.GetCookies(_client.BaseAddress);
            if (cookies["jwt"] != null)
            {
                cookies["jwt"].Expired = true;
            }
            // Alternatively, create a new HttpClient with no cookies
            var clientNoCookie = _fixture.Factory.CreateClient();

            // Manually create & store a valid refresh token for the user in the DB.
            string existingRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<IdentityContext>();
                var token = new RefreshToken
                {
                    UserId = userId!,
                    Token = existingRefreshToken,
                    Expires = DateTime.UtcNow.AddMinutes(30),
                    CreatedAt = DateTime.UtcNow
                };
                ctx.RefreshTokens.Add(token);
                await ctx.SaveChangesAsync();
            }

            // Act: call the refresh endpoint using clientNoCookie
            var refreshDto = new RefreshTokenRequest
            {
                AccessToken = oldAccessToken!,
                RefreshToken = existingRefreshToken
            };
            var refreshResponse = await clientNoCookie.PostAsJsonAsync("api/authentication/refresh-token", refreshDto);
            var refreshBody = await refreshResponse.Content.ReadAsStringAsync();

            // Assert
            Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

            var json = JObject.Parse(refreshBody);
            Assert.True(json.ContainsKey("accessToken"), "Response must contain 'accessToken'.");
            Assert.True(json.ContainsKey("refreshToken"), "Response must contain 'refreshToken'.");
            Assert.True(json.ContainsKey("expires"), "Response must contain 'expires'.");

            // Clean up the refresh token from the DB
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<IdentityContext>();
                var tokenToRemove = await ctx.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == existingRefreshToken);
                if (tokenToRemove != null)
                {
                    ctx.RefreshTokens.Remove(tokenToRemove);
                    await ctx.SaveChangesAsync();
                }
            }
        }

        [Fact(DisplayName = "[Cookie] POST /refresh-token - Failure (Invalid Tokens => Unauthorized)")]
        public async Task RefreshToken_InvalidTokens_ReturnsUnauthorized()
        {
            // Arrange
            var refreshDto = new RefreshTokenRequest
            {
                AccessToken = "fakeAccess",
                RefreshToken = "fakeRefresh"
            };

            // Act
            var response = await _client.PostAsJsonAsync("api/authentication/refresh-token", refreshDto);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact(DisplayName = "[Cookie] POST /refresh-token - Failure (Expired Refresh Token)")]
        public async Task RefreshToken_ExpiredRefreshToken_ReturnsUnauthorized()
        {
            // Arrange: register user, then create an expired refresh token
            var username = $"refresh_expired_{Guid.NewGuid()}";
            var password = "Password123!";
            var userId = await RegisterTestUserCookie(username, $"{username}@example.com", password);

            // Suppose we skip actual login, just assume the access token is valid for test
            string expiredToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<IdentityContext>();
                var token = new RefreshToken
                {
                    UserId = userId!,
                    Token = expiredToken,
                    Expires = DateTime.UtcNow.AddMinutes(-1), // already expired
                    CreatedAt = DateTime.UtcNow.AddHours(-2)
                };
                ctx.RefreshTokens.Add(token);
                await ctx.SaveChangesAsync();
            }

            var refreshDto = new RefreshTokenRequest
            {
                AccessToken = "someAccessToken",
                RefreshToken = expiredToken
            };

            // Act
            var response = await _client.PostAsJsonAsync("api/authentication/refresh-token", refreshDto);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        #endregion

        // ------------------------------------------------------
        // SECTION 2: Additional Bearer-based Tests
        // ------------------------------------------------------
        #region Bearer Tests

        [Fact(DisplayName = "[Bearer] POST /register => Get token => use Bearer => verify-token => 200")]
        public async Task Bearer_RegisterAndVerify()
        {
            // 1) Register a user, capturing the token from JSON
            var username = $"bearer_reg_{Guid.NewGuid()}";
            var password = "BearerPass123!";
            var regDto = new UserRegistrationDto
            {
                Username = username,
                Email = $"bearer_{username}@example.com",
                Password = password
            };

            var regResp = await _clientNoCookies.PostAsJsonAsync("api/authentication/register", regDto);
            Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);

            var regBody = await regResp.Content.ReadAsStringAsync();
            var regJson = JObject.Parse(regBody);
            var bearerToken = regJson["token"]?.ToString();
            Assert.False(string.IsNullOrEmpty(bearerToken), "Should receive a 'token' in the register response.");

            // 2) Set the Authorization header
            _clientNoCookies.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);

            // 3) Call /verify-token => Should be OK
            var verifyResp = await _clientNoCookies.GetAsync("api/authentication/verify-token");
            Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);

            var verifyBody = await verifyResp.Content.ReadAsStringAsync();
            var verifyJson = JObject.Parse(verifyBody);
            Assert.Equal(username, verifyJson["userName"]?.ToString());
        }

        [Fact(DisplayName = "[Bearer] POST /login => Bearer => verify-token => 200")]
        public async Task Bearer_LoginAndVerify()
        {
            // 1) Register
            var username = $"bearer_login_{Guid.NewGuid()}";
            var password = "BearerLogin123!";
            await RegisterTestUserCookie(username, $"{username}@example.com", password);

            // 2) Now login using clientNoCookies (we don't want any cookie set)
            var loginDto = new UserLoginDto
            {
                Username = username,
                Password = password
            };
            var loginResp = await _clientNoCookies.PostAsJsonAsync("api/authentication/login", loginDto);
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

            var loginBody = await loginResp.Content.ReadAsStringAsync();
            var loginJson = JObject.Parse(loginBody);
            var bearerToken = loginJson["token"]?.ToString();
            Assert.False(string.IsNullOrEmpty(bearerToken), "Login response must have 'token' field.");

            // 3) Set Bearer token
            _clientNoCookies.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);

            // 4) /verify-token => 200
            var verifyResp = await _clientNoCookies.GetAsync("api/authentication/verify-token");
            Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
            var verifyBody = await verifyResp.Content.ReadAsStringAsync();
            var verifyJson = JObject.Parse(verifyBody);
            Assert.Equal(username, verifyJson["userName"]?.ToString());
        }

        [Fact(DisplayName = "[Bearer] GET /verify-token => Invalid Bearer => 401")]
        public async Task Bearer_Verify_InvalidToken()
        {
            // Provide a random invalid bearer token
            _clientNoCookies.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "abc.def.ghi");

            // call verify-token => expect 401
            var verifyResp = await _clientNoCookies.GetAsync("api/authentication/verify-token");
            Assert.Equal(HttpStatusCode.Unauthorized, verifyResp.StatusCode);
        }

        [Fact(DisplayName = "[Bearer] POST /refresh-token => coverage")]
        public async Task Bearer_RefreshToken_Flow()
        {
            // 1) Register a user => parse out token
            var username = $"bearer_refresh_{Guid.NewGuid()}";
            var password = "BearerRefresh123!";
            var regDto = new UserRegistrationDto
            {
                Username = username,
                Email = $"{username}@example.com",
                Password = password
            };
            var regResp = await _clientNoCookies.PostAsJsonAsync("api/authentication/register", regDto);
            Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);

            var regBody = await regResp.Content.ReadAsStringAsync();
            var regJson = JObject.Parse(regBody);
            var oldAccessToken = regJson["token"]?.ToString();
            Assert.False(string.IsNullOrEmpty(oldAccessToken));

            // 2) Create a refresh token in DB manually
            string userId;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<IdentityContext>();
                var user = await ctx.Users.FirstOrDefaultAsync(u => u.UserName == username);
                Assert.NotNull(user);
                userId = user!.Id!;
            }

            var existingRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<IdentityContext>();
                var token = new RefreshToken
                {
                    UserId = userId,
                    Token = existingRefreshToken,
                    Expires = DateTime.UtcNow.AddMinutes(30),
                    CreatedAt = DateTime.UtcNow
                };
                ctx.RefreshTokens.Add(token);
                await ctx.SaveChangesAsync();
            }

            // 3) POST /refresh-token
            var refreshDto = new RefreshTokenRequest
            {
                AccessToken = oldAccessToken!,
                RefreshToken = existingRefreshToken
            };
            var refreshResp = await _clientNoCookies.PostAsJsonAsync("api/authentication/refresh-token", refreshDto);
            var refreshBody = await refreshResp.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);

            var refreshJson = JObject.Parse(refreshBody);
            Assert.True(refreshJson.ContainsKey("accessToken"), "Response must contain 'accessToken'.");
            Assert.True(refreshJson.ContainsKey("refreshToken"), "Response must contain 'refreshToken'.");
            Assert.True(refreshJson.ContainsKey("expires"), "Response must contain 'expires'.");
        }

        #endregion

        // ------------------------------------------------------
        // SECTION 3: Helper Methods
        // ------------------------------------------------------

        /// <summary>
        /// Registers a user using the /register endpoint (COOKIE client), asserting success, returns userId.
        /// </summary>
        private async Task<string?> RegisterTestUserCookie(string username, string email, string password)
        {
            var dto = new UserRegistrationDto
            {
                Username = username,
                Email = email,
                Password = password
            };
            var response = await _client.PostAsJsonAsync("api/authentication/register", dto);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);
            return json["userId"]?.ToString();
        }

        /// <summary>
        /// Logs in a user using /login endpoint (COOKIE client), asserting success.
        /// </summary>
        private async Task LoginTestUserCookie(string username, string password)
        {
            var dto = new UserLoginDto
            {
                Username = username,
                Password = password
            };
            var response = await _client.PostAsJsonAsync("api/authentication/login", dto);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
