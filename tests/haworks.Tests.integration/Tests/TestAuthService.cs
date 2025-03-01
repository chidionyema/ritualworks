using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using System.Linq;


namespace Haworks.Tests
{
    [Collection("Integration Tests")]
    public class TestAuthService
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly ITestOutputHelper _output;

        public TestAuthService(IntegrationTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public async Task Simplified_Authentication_Test()
        {
            // Create a custom factory with modified authentication handlers
            var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Output test configuration values
                    var config = _fixture.Configuration;
                    _output.WriteLine($"[CONFIG] JWT Issuer: {config["Jwt:Issuer"]}");
                    _output.WriteLine($"[CONFIG] JWT Audience: {config["Jwt:Audience"]}");
                    _output.WriteLine($"[CONFIG] JWT Key Length: {config["Jwt:Key"]?.Length ?? 0}");

                    // Register a test authorization handler that always succeeds for ContentUploader role
                    services.AddSingleton<IAuthorizationHandler, TestContentUploaderHandler>();
                });
            });

            // Create a client from the factory
            var client = factory.CreateClient();

            // Register a new user
            var username = $"test_auth_{Guid.NewGuid()}";
            var password = "Test1234!";
            var email = $"{username}@example.com";

            var registerDto = new
            {
                Username = username,
                Email = email,
                Password = password
            };

            _output.WriteLine($"Registering user: {username}");
            
            var registerResponse = await client.PostAsJsonAsync("/api/authentication/register", registerDto);
            registerResponse.EnsureSuccessStatusCode();
            
            var registerContent = await registerResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"Registration response: {registerContent}");

            // Parse token from response
            var responseJson = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(registerContent);
            string token = responseJson.token;
            _output.WriteLine($"Token: {token.Substring(0, Math.Min(30, token.Length))}...");

            // Create a new client with the token in header
            var authClient = factory.CreateClient();
            authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // First, try verify-token endpoint (which only needs authentication, not authorization)
            _output.WriteLine("Testing /api/authentication/verify-token endpoint...");
            var verifyResponse = await authClient.GetAsync("/api/authentication/verify-token");
            _output.WriteLine($"Verify-token status: {verifyResponse.StatusCode}");
            
            var verifyContent = await verifyResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"Verify-token response: {verifyContent}");

            // Then try the content endpoint which requires ContentUploader role
            var contentId = Guid.NewGuid();
            _output.WriteLine($"Testing protected endpoint: /api/v1/content/{contentId}");
            var contentResponse = await authClient.GetAsync($"/api/v1/content/{contentId}");
            _output.WriteLine($"Content endpoint status: {contentResponse.StatusCode}");
            
            Assert.False(contentResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized, 
                "Received 401 Unauthorized when it should be authenticated");
        }
    }

    /// <summary>
    /// A test handler that automatically succeeds for ContentUploader policy checks
    /// </summary>
    public class TestContentUploaderHandler : IAuthorizationHandler
    {
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            // Find any requirements related to ContentUploader role
            var pendingRequirements = context.PendingRequirements.ToArray();
            
            // Get the claims principal
            var identity = context.User.Identity;
            
            // Check if the user is authenticated
            if (identity != null && identity.IsAuthenticated)
            {
                // Check if this is a policy that requires ContentUploader role
                if (context.Resource?.ToString()?.Contains("ContentUploader") == true ||
                    pendingRequirements.Any(r => r.ToString()?.Contains("ContentUploader") == true))
                {
                    // Succeed all requirements since we're in test mode
                    foreach (var requirement in pendingRequirements)
                    {
                        context.Succeed(requirement);
                    }
                }
                
                // If the user has ContentUploader role, succeed all requirements
                if (context.User.IsInRole("ContentUploader"))
                {
                    foreach (var requirement in pendingRequirements)
                    {
                        context.Succeed(requirement);
                    }
                }
            }
            
            return Task.CompletedTask;
        }
    }
}