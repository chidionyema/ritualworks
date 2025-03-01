using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;
using System.Linq;

namespace Haworks.Tests
{
    [Collection("Integration Tests")]
    public class AuthenticationFixTest : IDisposable
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private CustomWebApplicationFactory _configuredFactory;

        public AuthenticationFixTest(IntegrationTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            
            // Create a new factory with debug settings and explicitly cast to CustomWebApplicationFactory
            _configuredFactory = (CustomWebApplicationFactory)_fixture.Factory.WithWebHostBuilder(builder => 
            {
                builder.ConfigureServices(services => 
                {
                    // Configure JWT validation with detailed logging
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => 
                    {
                        _output.WriteLine("Configuring JWT authentication for tests...");
                        
                        // Configure detailed debugging
                        options.SaveToken = true;
                        options.IncludeErrorDetails = true;
                        
                        // Log all JWT validation events 
                        options.Events = new JwtBearerEvents
                        {
                            OnAuthenticationFailed = context => 
                            {
                                _output.WriteLine($"[AUTH FAILED] Exception: {context.Exception}");
                                return Task.CompletedTask;
                            },
                            OnTokenValidated = context => 
                            {
                                _output.WriteLine($"[AUTH SUCCESS] Token validated for: {context.Principal?.Identity?.Name}");
                                if (context.SecurityToken is System.IdentityModel.Tokens.Jwt.JwtSecurityToken token)
                                {
                                    _output.WriteLine($"[TOKEN DETAILS] Issuer: {token.Issuer}");
                                    _output.WriteLine($"[TOKEN DETAILS] Audience: {token.Audiences.FirstOrDefault()}");
                                    _output.WriteLine($"[TOKEN DETAILS] Valid from: {token.ValidFrom}");
                                    _output.WriteLine($"[TOKEN DETAILS] Valid to: {token.ValidTo}");
                                    _output.WriteLine($"[TOKEN DETAILS] Claims: {string.Join(", ", token.Claims.Select(c => $"{c.Type}={c.Value}"))}");
                                }
                                return Task.CompletedTask;
                            },
                            OnChallenge = context => 
                            {
                                _output.WriteLine($"[AUTH CHALLENGE] Error: {context.Error}, Description: {context.ErrorDescription}");
                                return Task.CompletedTask;
                            },
                            OnMessageReceived = context => 
                            {
                                _output.WriteLine($"[AUTH MESSAGE] Token: {(context.Token?.Length > 20 ? context.Token?.Substring(0, 20) + "..." : context.Token)}");
                                return Task.CompletedTask;
                            }
                        };
                        
                        // Ensure validation parameters are set correctly
                        var config = _fixture.Configuration;
                        var key = config["Jwt:Key"];
                        _output.WriteLine($"[CONFIG] JWT Key: {(key?.Length > 10 ? key?.Substring(0, 10) + "..." : key)}");
                        _output.WriteLine($"[CONFIG] JWT Issuer: {config["Jwt:Issuer"]}");
                        _output.WriteLine($"[CONFIG] JWT Audience: {config["Jwt:Audience"]}");

                        // Set validation parameters explicitly
                        var keyBytes = Convert.FromBase64String(key ?? throw new InvalidOperationException("JWT key not found in configuration"));
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = config["Jwt:Issuer"],
                            ValidateAudience = true,
                            ValidAudience = config["Jwt:Audience"],
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                            ClockSkew = TimeSpan.Zero
                        };
                    });
                });
            });
        }

        public void Dispose()
        {
            _configuredFactory?.Dispose();
        }

        [Fact]
        public async Task Full_JWT_Authentication_Flow_Test()
        {
            // Create a clean HTTP client for registration
            var client = _configuredFactory.CreateClient();

            // ------------------------------------------------
            // 1. REGISTER USER
            // ------------------------------------------------
            var username = $"auth_test_{Guid.NewGuid()}";
            var password = "StrongP@ss123!";
            var email = $"{username}@example.com";
            
            var registerDto = new
            {
                Username = username,
                Email = email,
                Password = password
            };

            _output.WriteLine($"Registering user: {username}");
            var registerJson = JsonConvert.SerializeObject(registerDto);
            _output.WriteLine($"Registration payload: {registerJson}");
            
            var registerResponse = await client.PostAsJsonAsync("/api/authentication/register", registerDto);
            
            var registerContent = await registerResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"Registration response status: {(int)registerResponse.StatusCode} {registerResponse.StatusCode}");
            _output.WriteLine($"Registration response content: {registerContent}");
            
            registerResponse.EnsureSuccessStatusCode();
            
            // ------------------------------------------------
            // 2. EXTRACT TOKEN FROM RESPONSE
            // ------------------------------------------------
            var responseObj = JObject.Parse(registerContent);
            string token = responseObj["token"].ToString();
            string userId = responseObj["userId"].ToString();
            
            _output.WriteLine($"Extracted token: {token.Substring(0, Math.Min(token.Length, 50))}...");
            _output.WriteLine($"User ID: {userId}");
            
            // Decode and print token payload for debugging
            var tokenParts = token.Split('.');
            if (tokenParts.Length >= 2)
            {
                var payloadBase64 = tokenParts[1];
                // Add padding if needed
                while (payloadBase64.Length % 4 != 0)
                {
                    payloadBase64 += "=";
                }
                var payloadBytes = Convert.FromBase64String(payloadBase64);
                var payloadJson = Encoding.UTF8.GetString(payloadBytes);
                _output.WriteLine($"JWT payload: {payloadJson}");
            }
            
            // ------------------------------------------------
            // 3. VERIFY TOKEN WORKS WITH VERIFY-TOKEN ENDPOINT
            // ------------------------------------------------
            var verifyClient = _configuredFactory.CreateClient();
            verifyClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            _output.WriteLine("Verifying token with /api/authentication/verify-token");
            var verifyResponse = await verifyClient.GetAsync("/api/authentication/verify-token");
            
            var verifyContent = await verifyResponse.Content.ReadAsStringAsync();
            _output.WriteLine($"Verify response status: {(int)verifyResponse.StatusCode} {verifyResponse.StatusCode}");
            _output.WriteLine($"Verify response content: {verifyContent}");
            
            if (!verifyResponse.IsSuccessStatusCode)
            {
                _output.WriteLine("!!!TOKEN VERIFICATION FAILED!!!");
                throw new Exception($"Token verification failed: {verifyResponse.StatusCode}");
            }
            
            // ------------------------------------------------
            // 4. ACCESS PROTECTED ENDPOINT
            // ------------------------------------------------
            var protectedClient = _configuredFactory.CreateClient();
            
            // Ensure authorization header is set properly
            protectedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _output.WriteLine($"Authorization header: {protectedClient.DefaultRequestHeaders.Authorization}");
            
            // Try to access a protected endpoint
            var contentId = Guid.NewGuid();
            _output.WriteLine($"Accessing protected endpoint: /api/v1/content/{contentId}");
            
            var protectedResponse = await protectedClient.GetAsync($"/api/v1/content/{contentId}");
            var protectedContent = await protectedResponse.Content.ReadAsStringAsync();
            
            _output.WriteLine($"Protected endpoint response: {(int)protectedResponse.StatusCode} {protectedResponse.StatusCode}");
            _output.WriteLine($"Protected endpoint content: {protectedContent}");
            
            // We expect a 404 Not Found since the content doesn't exist
            // But we should NOT get 401 Unauthorized
            Assert.NotEqual(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
            
            if (protectedResponse.StatusCode == HttpStatusCode.NotFound)
            {
                _output.WriteLine("TEST PASSED: Authentication successful, got expected 404 Not Found (content ID doesn't exist)");
            }
            else if (protectedResponse.IsSuccessStatusCode)
            {
                _output.WriteLine("TEST PASSED: Authentication successful, protected endpoint returned success");
            }
            else
            {
                _output.WriteLine($"TEST FAILED: Got unexpected status code: {protectedResponse.StatusCode}");
                throw new Exception($"Protected endpoint returned unexpected status: {protectedResponse.StatusCode}");
            }
        }
    }
}
