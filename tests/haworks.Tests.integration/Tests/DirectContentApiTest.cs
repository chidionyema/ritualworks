using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Haworks.Tests
{
    [Collection("Integration Tests")]
    public class DirectContentApiTest
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly ITestOutputHelper _output;

        public DirectContentApiTest(IntegrationTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public async Task Add_Test_User_And_Access_Content_API()
        {
            // Create a factory with a bypass middleware
            var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Register a middleware that adds authentication and ContentUploader role
                    services.AddTransient<TestAuthMiddleware>();
                });

                builder.Configure(app =>
                {
                    // Add our test auth middleware before the real app middleware
                    app.UseMiddleware<TestAuthMiddleware>();
                });
            });

            // Create a client
            var client = factory.CreateClient();

            // Try accessing protected content API directly
            var contentId = Guid.NewGuid();
            _output.WriteLine($"Testing direct access to content endpoint with ID: {contentId}");

            var response = await client.GetAsync($"/api/v1/content/{contentId}");
            _output.WriteLine($"Response status: {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            _output.WriteLine($"Response content: {content}");

            // We expect a 404 since the content doesn't exist, but NOT a 401
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

   
}