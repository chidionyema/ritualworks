using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Castle.Core;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using RitualWorks;
using RitualWorks.Db;
using Xunit;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace RitualWorks.Tests
{
    public class IntegrationTestFixture : IAsyncLifetime
    {
        private readonly DockerHelper _postgresHelper;
        private readonly DockerHelper _rabbitMqHelper;
        private readonly DockerHelper _azuriteHelper;

        public HttpClient Client { get; private set; }
        public WebApplicationFactory<Program> Factory { get; private set; }
        private readonly ILogger<DockerHelper> _logger;

        public IntegrationTestFixture()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });
            _logger = loggerFactory.CreateLogger<DockerHelper>();

            // Initialize DockerHelper for PostgreSQL
            _postgresHelper = new DockerHelper(_logger);
            ConfigurePostgresHelper();

            // Initialize DockerHelper for RabbitMQ
            _rabbitMqHelper = new DockerHelper(_logger);
            ConfigureRabbitMqHelper();

            // Initialize DockerHelper for Azurite (Azure Storage Emulator)
            _azuriteHelper = new DockerHelper(_logger);
            ConfigureAzuriteHelper();
        }

        private void ConfigurePostgresHelper()
        {
            // Setting the PostgreSQL Docker configuration directly in the StartContainer call
            _postgresHelper.StartContainer(5439).GetAwaiter().GetResult();
        }

        private void ConfigureRabbitMqHelper()
        {
            // Configure and start RabbitMQ container
            _rabbitMqHelper.StartContainer(5672).GetAwaiter().GetResult();
        }

        private void ConfigureAzuriteHelper()
        {
            // Configure and start Azurite container
            _azuriteHelper.StartContainer(10000).GetAwaiter().GetResult();
        }

        public async Task InitializeAsync()
        {
            await _postgresHelper.StartContainer();
            await _rabbitMqHelper.StartContainer();
            // await _azuriteHelper.StartContainer();

            Factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    var projectDir = GetProjectPath();
                    var configPath = Path.Combine(projectDir, "tests", "RitualWorks.Tests.integration");

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
                                options.UseNpgsql($"Host=localhost;Port={5439};Database=test_db;Username=myuser;Password=mypassword"));

                        services.AddAuthentication("Test")
                            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });

                        var sp = services.BuildServiceProvider();

                        using (var scope = sp.CreateScope())
                        {
                            var scopedServices = scope.ServiceProvider;
                            var db = scopedServices.GetRequiredService<RitualWorksContext>();
                            db.Database.Migrate();
                            SeedData(scopedServices).Wait();
                        }
                    });
                });

            Client = Factory.CreateClient();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        }

        private async Task SeedData(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<RitualWorksContext>();

            var category = new Category(Guid.NewGuid(), "Test Category");
            context.Categories.Add(category);

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
            await _postgresHelper.StopContainer();
            await _rabbitMqHelper.StopContainer();
            // await _azuriteHelper.StopContainer();
            Factory?.Dispose();
            Client?.Dispose();
        }
    }

    [CollectionDefinition("Integration Tests")]
    public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture> { }
}
