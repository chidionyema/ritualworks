using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using haworks.Controllers;
using haworks.Db;
using haworks.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;
using Haworks.Infrastructure.Data;

namespace Haworks.Tests.Integration
{
    [Collection("Integration Tests")]
    public class ExternalAuthenticationControllerTests : IAsyncLifetime
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly HttpClient _client;

        public ExternalAuthenticationControllerTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClientWithCookies();
        }

        public async Task InitializeAsync()
        {
            await _fixture.ResetDatabaseAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        #region Challenge Tests

        [Fact(DisplayName = "GET /challenge/{provider} - Initiates External Auth Challenge")]
        public async Task Challenge_InitiatesExternalAuthFlow()
        {
            // Arrange - Google is the test provider
            var provider = "Google";
            var redirectUrl = "http://localhost/callback";

            // Act - Call the challenge endpoint
            var response = await _client.GetAsync($"api/external-authentication/challenge/{provider}?redirectUrl={redirectUrl}");

            // Assert - Should return a redirect to the Google auth page
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            
            // The Location header should point to a URL containing "accounts.google.com"
            var locationHeader = response.Headers.Location?.ToString();
            Assert.NotNull(locationHeader);
            Assert.Contains("accounts.google.com", locationHeader);
        }

       [Fact(DisplayName = "GET /challenge/{provider} - Works Without Explicit Redirect URL")]
        public async Task Challenge_WorksWithoutRedirectUrl()
        {
            // First, get the list of available providers to confirm what's configured
            var providersResponse = await _client.GetAsync("api/external-authentication/providers");
            var providersContent = await providersResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"Available providers: {providersContent}");
            
            // Use the exact provider name from the response
            var provider = "Google"; 
            var response = await _client.GetAsync($"api/external-authentication/challenge/{provider}");
            
            Console.WriteLine($"Challenge response: {response.StatusCode}");
            
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            
            var locationHeader = response.Headers.Location?.ToString();
            Assert.NotNull(locationHeader);
            Assert.Contains("accounts.google.com", locationHeader);
}

        [Fact(DisplayName = "GET /challenge/{provider} - Returns BadRequest for Invalid Provider")]
        public async Task Challenge_ReturnsError_ForInvalidProvider()
        {
            // Arrange - Use a non-existent provider
            var provider = "InvalidProvider";
            var redirectUrl = "http://localhost/callback";

            // Act - Call the challenge endpoint with a client that doesn't auto-redirect
            var response = await _client.GetAsync($"api/external-authentication/challenge/{provider}?redirectUrl={redirectUrl}");

            // Assert - Should return an error (either BadRequest or NotFound depending on your implementation)
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                        response.StatusCode == HttpStatusCode.NotFound);
        }

        #endregion

        #region Callback Tests

        [Fact(DisplayName = "GET /callback - Creates New User From External Login")]
        public async Task Callback_CreatesNewUser_FromExternalLogin()
        {
            // Arrange - Set up a client with external auth simulation
            var email = "newuser@example.com";
            var name = "New_User";
            var providerKey = "google-new-user-12345";
            
            var client = await SimulateExternalLogin("Google", providerKey, name, email);

            // Act - Call the callback endpoint
            var response = await client.GetAsync("api/external-authentication/callback");

            // Assert - Should successfully create a new user
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseBody = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseBody);
            
            // Token should be in the response
            Assert.NotNull(json["token"]);
            
            // User should have been created with the right properties
            Assert.NotNull(json["user"]);
            Assert.Equal(name, json["user"]["userName"]);
            Assert.Equal(email, json["user"]["email"]);
            
            // Verify user exists in the database
            var userId = json["user"]["id"].ToString();
            var user = await GetUserById(userId);
            Assert.NotNull(user);
            Assert.Equal(email, user.Email);
            
            // Verify external login was added
            var userLogins = await GetUserLogins(userId);
            Assert.Single(userLogins);
            Assert.Equal("Google", userLogins.First().LoginProvider);
            Assert.Equal(providerKey, userLogins.First().ProviderKey);
        }

        [Fact(DisplayName = "GET /callback - Adds Login To Existing User")]
        public async Task Callback_AddsLogin_ToExistingUser()
        {
            // Arrange - First create a user
            var uniqueId = Guid.NewGuid().ToString();
            var username = $"existing_user_{uniqueId}";
            var email = $"existing_{uniqueId}@example.com";
            var password = "StrongPassword123!";


            var userId = await RegisterTestUser(username, email, password);
            
            // Simulate external login with the same email
            var client = await SimulateExternalLogin("Google", "google-existing-12345", "External_User", email);

            // Act - Call the callback endpoint
            var response = await client.GetAsync("api/external-authentication/callback");

            // Assert - Should add external login to existing user
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseBody = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseBody);
            
            // Should return token
            Assert.NotNull(json["token"]);
            
            // Should be the existing user
            Assert.NotNull(json["user"]);
            Assert.Equal(userId, json["user"]["id"].ToString());
            Assert.Equal(username, json["user"]["userName"]);
            Assert.Equal(email, json["user"]["email"]);
            
            // Verify login was added to the user
            var userLogins = await GetUserLogins(userId);
            Assert.Single(userLogins);
            Assert.Equal("Google", userLogins.First().LoginProvider);
        }

        [Fact(DisplayName = "GET /callback - Signs In With Existing External Login")]
        public async Task Callback_SignsIn_WithExistingExternalLogin()
        {
            // Arrange - Create a user with external login
            var email = "extuser@example.com";
            var name = "External_User";
            var providerKey = "google-existing-login-12345";
            
            // First create the user with external login
            var firstClient = await SimulateExternalLogin("Google", providerKey, name, email);
            var firstResponse = await firstClient.GetAsync("api/external-authentication/callback");
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            var firstJson = JObject.Parse(await firstResponse.Content.ReadAsStringAsync());
            var userId = firstJson["user"]["id"].ToString();
            
            // Now simulate a second login with the same external provider
            var secondClient = await SimulateExternalLogin("Google", providerKey, name, email);
            
            // Act - Call the callback endpoint again
            var response = await secondClient.GetAsync("api/external-authentication/callback");
            
            // Assert - Should sign in with the existing user
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            
            var responseBody = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseBody);
            
            // Should return a token
            Assert.NotNull(json["token"]);
            
            // Should be the same user
            Assert.NotNull(json["user"]);
            Assert.Equal(userId, json["user"]["id"].ToString());
            Assert.Equal(name, json["user"]["userName"]);
            Assert.Equal(email, json["user"]["email"]);
        }

        [Fact(DisplayName = "GET /callback - Returns BadRequest When Missing Email")]
        public async Task Callback_ReturnsBadRequest_WhenMissingEmail()
        {
            // Arrange - Simulate external login without email
            var client = await SimulateExternalLogin("Google", "google-no-email-12345", "No Email User", null);
            
            // Act - Call the callback endpoint
            var response = await client.GetAsync("api/external-authentication/callback");
            
            // Assert - Should return BadRequest due to missing email
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Contains("Email not provided", responseBody);
        }

        [Fact(DisplayName = "GET /callback - Returns BadRequest When External Info is Null")]
        public async Task Callback_ReturnsBadRequest_WhenExternalInfoIsNull()
        {
            // Act - Call the callback endpoint without external login info
            var response = await _client.GetAsync("api/external-authentication/callback");
            
            // Assert - Should return BadRequest
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Contains("Error getting external login information", responseBody);
        }

        [Fact(DisplayName = "GET /callback - Creates User with Default Role")]
        public async Task Callback_CreatesUser_WithDefaultRole()
        {
            // Arrange - Set up external login
            var email = "roleuser@example.com";
            var name = "Role_User";
            var providerKey = "google-role-test-12345";
            
            var client = await SimulateExternalLogin("Google", providerKey, name, email);

            // Act - Call the callback endpoint
            var response = await client.GetAsync("api/external-authentication/callback");

            // Assert - Should create user successfully
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            
            var responseBody = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseBody);
            var userId = json["user"]["id"].ToString();
            
            // Verify user has the default role
            var user = await GetUserById(userId);
            var roles = await GetUserRoles(user);
            
            Assert.Contains("ContentUploader", roles);
        }

        [Fact(DisplayName = "GET /callback - Creates User with Default Claims")]
        public async Task Callback_CreatesUser_WithDefaultClaims()
        {
            // Arrange - Set up external login
            var email = "claimuser@example.com";
            var name = "Claim_User";
            var providerKey = "google-claim-test-12345";
            
            var client = await SimulateExternalLogin("Google", providerKey, name, email);

            // Act - Call the callback endpoint
            var response = await client.GetAsync("api/external-authentication/callback");

            // Assert - Should create user successfully
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            
            var responseBody = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseBody);
            var userId = json["user"]["id"].ToString();
            
            // Verify user has the default claim
            var user = await GetUserById(userId);
            var claims = await GetUserClaims(user);
            
            Assert.Contains(claims, c => c.Type == "permission" && c.Value == "upload_content");
        }

        #endregion

        #region Available Providers Tests

        [Fact(DisplayName = "GET /providers - Returns Available Authentication Providers")]
        public async Task GetAvailableProviders_ReturnsAuthenticationSchemes()
        {
            // Act - Call the providers endpoint
            var response = await _client.GetAsync("api/external-authentication/providers");
            
            // Assert - Should return OK with a list of providers
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            
            var responseBody = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseBody);
            var providers = json["providers"] as JArray;
            
            Assert.NotNull(providers);
            Assert.NotEmpty(providers);
            
            // Use camelCase for property names
            var providerNames = providers.Select(p => p["name"].ToString()).ToList();
            Assert.Contains("Google", providerNames);
            Assert.Contains("Facebook", providerNames);
        }

        #endregion

        #region User Login Management Tests

        [Fact(DisplayName = "GET /logins - Returns User's External Logins")]
        public async Task GetUserLogins_ReturnsExternalLogins()
        {
            // Arrange - Create a user
            var username = "logins_test_user";
            var email = "logins@example.com";
            var password = "StrongPassword123!";

            var userId = await RegisterAndLoginTestUser(username, email, password);

            // Call the logins endpoint to get initial state
            var response = await _client.GetAsync("api/external-authentication/logins");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var responseBody = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseBody);
            var logins = json["Logins"] as JArray;
            Assert.NotNull(logins);
            Assert.Empty(logins);

            // Add a mock external login to the user
            await AddExternalLogin(userId, "Google", "google-12345", "Google");

            // Act - Call logins endpoint again
            response = await _client.GetAsync("api/external-authentication/logins");
            
            // Assert - Should now show the Google login
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            responseBody = await response.Content.ReadAsStringAsync();
            json = JObject.Parse(responseBody);
            logins = json["Logins"] as JArray;
            Assert.NotNull(logins);
            Assert.Single(logins);
            Assert.Equal("Google", logins[0]["Provider"]);
        }

        [Fact(DisplayName = "GET /logins - Returns Unauthorized When Not Authenticated")]
        public async Task GetUserLogins_ReturnsUnauthorized_WhenNotAuthenticated()
        {
            // Arrange - Create a client without authentication
            var client = _fixture.CreateClientWithCookies();
            
            // Act - Call the logins endpoint
            var response = await client.GetAsync("api/external-authentication/logins");
            
            // Assert - Should return Unauthorized
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

         // In ExternalAuthenticationControllerTests.cs
    [Fact(DisplayName = "DELETE /unlink/{provider} - Removes External Login")]
public async Task UnlinkLogin_RemovesExternalLogin()
{
    // Arrange 
    var username = "unlink_test_user";
    var email = "unlink@example.com";
    var password = "StrongPassword123!";

    var userId = await RegisterAndLoginTestUser(username, email, password);
    
    // Add external login with the exact provider name
    await AddExternalLogin(userId, "Google", "google-unlink-12345", "Google");
    
    // Verify login was added
    var initialLogins = await GetUserLogins(userId);
    Assert.Single(initialLogins);
    Assert.Equal("Google", initialLogins.First().LoginProvider);

    // Make verify-token request to confirm authentication
    var verifyResponse = await _client.GetAsync("api/authentication/verify-token");
    Console.WriteLine($"Verify response status: {verifyResponse.StatusCode}");
    Console.WriteLine($"Verify response body: {await verifyResponse.Content.ReadAsStringAsync()}");
    
    // Act
    var response = await _client.DeleteAsync("api/external-authentication/unlink/Google");

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    // Verify removal
    var finalLogins = await GetUserLogins(userId);
    Assert.Empty(finalLogins);
}

        [Fact(DisplayName = "DELETE /unlink/{provider} - Returns BadRequest When Provider Not Found")]
        public async Task UnlinkLogin_ReturnsBadRequest_WhenProviderNotFound()
        {
            // Arrange - Create a user without external logins
            var username = "no_ext_login_user";
            var email = "no_ext@example.com";
            var password = "StrongPassword123!";

            await RegisterAndLoginTestUser(username, email, password);
            
            // Act - Try to unlink a non-existent provider
            var response = await _client.DeleteAsync("api/external-authentication/unlink/Google");
            
            // Assert - Should return BadRequest
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Contains("No Google login found", responseBody);
        }

        [Fact(DisplayName = "DELETE /unlink/{provider} - Returns Unauthorized When Not Authenticated")]
        public async Task UnlinkLogin_ReturnsUnauthorized_WhenNotAuthenticated()
        {
            // Arrange - Create a client without authentication
            var client = _fixture.CreateClientWithCookies();
            
            // Act - Call the unlink endpoint
            var response = await client.DeleteAsync("api/external-authentication/unlink/Google");
            
            // Assert - Should return Unauthorized
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact(DisplayName = "POST /link/{provider} - Initiates External Login Link")]
        public async Task LinkExternalLogin_InitiatesExternalLoginLink()
        {
            // Arrange - Create and login a user
            var username = "link_test_user";
            var email = "link@example.com";
            var password = "StrongPassword123!";

            await RegisterAndLoginTestUser(username, email, password);
            
            // Act - Call the link endpoint
            var response = await _client.PostAsync("api/external-authentication/link/Google", null);
            
            // Assert - Should redirect to the Google auth page
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            
            var locationHeader = response.Headers.Location?.ToString();
            Assert.NotNull(locationHeader);
            Assert.Contains("accounts.google.com", locationHeader);
        }

        [Fact(DisplayName = "POST /link/{provider} - Returns Unauthorized When Not Authenticated")]
        public async Task LinkExternalLogin_ReturnsUnauthorized_WhenNotAuthenticated()
        {
            // Arrange - Create a client without authentication
            var client = _fixture.CreateClientWithCookies();
            
            // Act - Call the link endpoint
            var response = await client.PostAsync("api/external-authentication/link/Google", null);
            
            // Assert - Should return Unauthorized
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact(DisplayName = "GET /link-callback - Links External Login To User")]
        public async Task LinkCallback_LinksExternalLogin_ToUser()
        {
            // This test would require significant mocking of the auth flow and state
            // It's one of the most challenging parts to test without significant integration
            // Consider mocking this particular test or implementing a custom auth handler
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a test client with simulated external authentication
        /// </summary>
        private async Task<HttpClient> SimulateExternalLogin(string provider, string providerKey, string name, string email)
        {
            // Create a custom client using TestServer's authentication simulation capabilities
            var client = _fixture.CreateClientWithCookies();
            
            // Configure the test authentication handler to simulate an external login
            await ConfigureTestAuthHandler(client, provider, providerKey, name, email);
            
            return client;
        }
        
        /// <summary>
        /// Configures the test authentication handler to simulate an external login
        /// </summary>
        private async Task ConfigureTestAuthHandler(HttpClient client, string provider, string providerKey, string name, string email)
        {
            // Set up authentication cookies that will be used by the handler in the test server
            var authUrl = $"api/test-auth/simulate-external-login?provider={provider}&providerKey={providerKey}";
            
            if (!string.IsNullOrEmpty(name))
            {
                authUrl += $"&name={Uri.EscapeDataString(name)}";
            }
            
            if (!string.IsNullOrEmpty(email))
            {
                authUrl += $"&email={Uri.EscapeDataString(email)}";
            }
            
            // Call the test auth endpoint to set up the cookies
            var response = await client.GetAsync(authUrl);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Registers a test user and returns the user ID
        /// </summary>
      private async Task<string> RegisterTestUser(string username, string email, string password)
{
    // Create registration request
    var registrationData = new
    {
        Username = username,
        Email = email,
        Password = password,
        ConfirmPassword = password 
    };

   var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    var content = new StringContent(JsonSerializer.Serialize(registrationData, options), 
        Encoding.UTF8, "application/json");
    // Call registration endpoint
    var response = await _client.PostAsync("api/authentication/register", content);

    // Log response for debugging - Fix the formatting error
    var responseBody = await response.Content.ReadAsStringAsync();

    response.EnsureSuccessStatusCode();

    // Extract user ID from response - check for both possible property names
    var json = JObject.Parse(responseBody);
    string userId = null;
    
    // Try different possible property names
    if (json["userId"] != null)
        userId = json["userId"].ToString();
    else if (json["id"] != null)
        userId = json["id"].ToString();
    
    if (string.IsNullOrEmpty(userId))
    {
        throw new InvalidOperationException($"Expected user ID in response but got: {responseBody}");
    }
    
    return userId;
}

        /// <summary>
        /// Registers a test user, logs them in, and returns the user ID
        /// </summary>
        private async Task<string> RegisterAndLoginTestUser(string username, string email, string password)
{
    // Register the user
    var userId = await RegisterTestUser(username, email, password);
    
    // Login the user - use username exactly as the controller expects
    var loginData = new 
    {
        Username = username, // Controller expects Username, not Email
        Password = password
    };
    
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    var content = new StringContent(JsonSerializer.Serialize(loginData, options), 
        Encoding.UTF8, "application/json");
    
    var response = await _client.PostAsync("api/authentication/login", content);
    var responseBody = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Login response status: {response.StatusCode}");
    
    // This will throw if not successful
    response.EnsureSuccessStatusCode();
    
    // Extract the JWT token from the response
    var json = JObject.Parse(responseBody);
    var token = json["token"].ToString();
    
    // Set the bearer token for subsequent requests
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    
    // Make a verification request to confirm authorization
    var verifyResponse = await _client.GetAsync("api/authentication/verify-token");
    Console.WriteLine($"Verify response status: {verifyResponse.StatusCode}");
    
    if (verifyResponse.IsSuccessStatusCode)
    {
        var verifyBody = await verifyResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"Verify response body: {verifyBody}");
    }
    
    return userId;
}

        /// <summary>
        /// Adds an external login to a user
        /// </summary>
        private async Task AddExternalLogin(string userId, string provider, string providerKey, string displayName)
        {
            // Access the UserManager directly through the test server
            using var scope = _fixture.Factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            
            var user = await userManager.FindByIdAsync(userId);
            Assert.NotNull(user);
            
            var login = new UserLoginInfo(provider, providerKey, displayName);
            var result = await userManager.AddLoginAsync(user, login);
            
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        /// <summary>
        /// Gets a user by ID
        /// </summary>
        private async Task<User> GetUserById(string userId)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            
            return await userManager.FindByIdAsync(userId);
        }

        /// <summary>
        /// Gets a user's external logins
        /// </summary>
        private async Task<IList<UserLoginInfo>> GetUserLogins(string userId)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            
            var user = await userManager.FindByIdAsync(userId);
            Assert.NotNull(user);
            
            return await userManager.GetLoginsAsync(user);
        }

        /// <summary>
        /// Gets a user's roles
        /// </summary>
        private async Task<IList<string>> GetUserRoles(User user)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            
            return await userManager.GetRolesAsync(user);
        }

        /// <summary>
        /// Gets a user's claims
        /// </summary>
        private async Task<IList<Claim>> GetUserClaims(User user)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            
            return await userManager.GetClaimsAsync(user);
        }

        #endregion
    }
}