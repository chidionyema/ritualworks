using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RitualWorks;
using RitualWorks.Db;
using Xunit;

namespace RitualWorks.Tests
{
    public class IntegrationTestFixture : IAsyncLifetime
    {
        private readonly DockerHelper _dockerHelper = new DockerHelper();
        public HttpClient Client { get; private set; }
        public WebApplicationFactory<Program> Factory { get; private set; }

        public async Task InitializeAsync()
        {
            await _dockerHelper.StartContainer(5439);

            Factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    var projectDir = GetProjectPath();
                    var configPath = Path.Combine(projectDir, "src");

                    if (!Directory.Exists(configPath))
                    {
                        throw new DirectoryNotFoundException($"The configuration path '{configPath}' does not exist.");
                    }

                    builder.UseSetting("ContentRoot", configPath);
                    builder.ConfigureAppConfiguration((context, config) =>
                    {
                        config.SetBasePath(configPath);
                        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    });

                    builder.ConfigureServices(services =>
                    {
                        services.AddEntityFrameworkNpgsql()
                            .AddDbContext<RitualWorksContext>(options =>
                                options.UseNpgsql("Host=localhost;Port=5439;Database=test_db;Username=test_user;Password=test_password"));

                        // Add test authentication handler
                        services.AddAuthentication("Test")
                            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });

                        var sp = services.BuildServiceProvider();

                        // Apply migrations and seed data
                        using (var scope = sp.CreateScope())
                        {
                            var scopedServices = scope.ServiceProvider;
                            var db = scopedServices.GetRequiredService<RitualWorksContext>();
                            db.Database.Migrate();
                            SeedData(scopedServices).Wait();  // Seed the data
                        }
                    });
                });

            Client = Factory.CreateClient();
        }

        private async Task SeedData(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<RitualWorksContext>();

            // Add initial categories
            var category = new Category(Guid.NewGuid(), "Test Category");
            context.Categories.Add(category);

            // Add initial products
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = "Test Product",
                Description = "Test Description",
                Price = 9.99M,
                Stock = 10,
                CategoryId = category.Id
            };
            context.Products.Add(product);

            // Add a test user
            var testUser = new User
            {
                Id = "test-user-id",
                UserName = "testuser",
                Email = "testuser@example.com",
                EmailConfirmed = true,
                NormalizedUserName = "TESTUSER",
                NormalizedEmail = "TESTUSER@EXAMPLE.COM",
                SecurityStamp = Guid.NewGuid().ToString("D")
            };

            if (!context.Users.Any(u => u.Id == testUser.Id))
            {
                context.Users.Add(testUser);
            }

            await context.SaveChangesAsync();
        }

        private string GetProjectPath()
        {
            var currentDirectory = AppContext.BaseDirectory;
            var directoryInfo = new DirectoryInfo(currentDirectory);

            while (directoryInfo != null && !File.Exists(Path.Combine(directoryInfo.FullName, "RitualWorks.sln")))
            {
                directoryInfo = directoryInfo.Parent;
            }

            if (directoryInfo == null)
            {
                throw new DirectoryNotFoundException("Solution directory could not be found.");
            }

            return directoryInfo.FullName;
        }

        public async Task DisposeAsync()
        {
            await _dockerHelper.StopContainer();
            Factory?.Dispose();
            Client?.Dispose();
        }
    }

    [CollectionDefinition("Integration Tests")]
    public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture> { }
}
