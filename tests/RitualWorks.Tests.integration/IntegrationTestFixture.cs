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
        private readonly DockerHelper _minioHelper;

        public HttpClient Client { get; private set; }
        public WebApplicationFactory<Program> Factory { get; private set; }
        private readonly ILogger<DockerHelper> _logger;

        // Connection string built from environment variables
        private readonly string _connectionString;

        public IntegrationTestFixture()
        {
            // Read environment variables for connection string components
            var dbHost = "localhost";
            var dbPort = "5432";
            var dbName = "test_db";
            var dbUser = "myuser";
            var dbPassword = "mypassword";

            // Construct the connection string using the environment variables
            _connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword};";

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });
            _logger = loggerFactory.CreateLogger<DockerHelper>();

            _postgresHelper = new DockerHelper(
                _logger,
                imageName: "postgres:13",
                containerName: "postgres_test_container",
                serviceStartTimeout: 60000,
                postStartCallback: async (port) =>
                {
                    await WaitForPostgresToBeReadyAsync(port);
                    await EnsureDatabaseExistsAsync(port);
                    await SetupDatabaseAsync(port);
                }
            );
            ConfigurePostgresHelper();

            _rabbitMqHelper = new DockerHelper(
                _logger,
                imageName: "rabbitmq:3-management",
                containerName: "rabbitmq_test_container",
                serviceStartTimeout: 60000
            );
            ConfigureRabbitMqHelper();

            _minioHelper = new DockerHelper(
                _logger,
                imageName: "minio/minio",
                containerName: "minio_test_container",
                serviceStartTimeout: 60000
            );
            ConfigureMinioHelper();
        }

        private void ConfigurePostgresHelper()
        {
            _postgresHelper.StartContainer(
                hostPort: 5432,
                containerPort: 5432,
                environmentVariables: new List<string>
                {
                    "POSTGRES_USER=myuser",
                    "POSTGRES_PASSWORD=mypassword",
                    "POSTGRES_DB=test_db"
                }
            ).GetAwaiter().GetResult();
        }

        private void ConfigureRabbitMqHelper()
        {
            _rabbitMqHelper.StartContainer(
                hostPort: 5672,
                containerPort: 5672,
                environmentVariables: new List<string>
                {
                    "RABBITMQ_DEFAULT_USER=guest",
                    "RABBITMQ_DEFAULT_PASS=guest"
                }
            ).GetAwaiter().GetResult();
        }

        private void ConfigureMinioHelper()
        {
            _minioHelper.StartContainer(
                hostPort: 9000,
                containerPort: 9000,
                environmentVariables: new List<string>
                {
                    "MINIO_ROOT_USER=minioadmin",
                    "MINIO_ROOT_PASSWORD=minioadmin"
                },
                command: new List<string> { "server", "/data" }
            ).GetAwaiter().GetResult();
        }

        public async Task InitializeAsync()
        {
            await _postgresHelper.StartContainer(5432, 5432, new List<string> { "POSTGRES_USER=myuser", "POSTGRES_PASSWORD=mypassword", "POSTGRES_DB=test_db" });
            await _rabbitMqHelper.StartContainer(5672, 5672, new List<string> { "RABBITMQ_DEFAULT_USER=guest", "RABBITMQ_DEFAULT_PASS=guest" });
            await _minioHelper.StartContainer(9000, 9000, new List<string> { "MINIO_ROOT_USER=minioadmin", "MINIO_ROOT_PASSWORD=minioadmin" }, new List<string> { "server", "/data" });

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
                                options.UseNpgsql(_connectionString));

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

        private async Task WaitForPostgresToBeReadyAsync(int port)
        {
            _logger.LogInformation("Waiting for PostgreSQL to initialize...");
            var timeout = Task.Delay(60000);

            while (!timeout.IsCompleted)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(_connectionString);
                    await connection.OpenAsync();
                    _logger.LogInformation("PostgreSQL container is ready.");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Waiting for PostgreSQL to be ready: {ex.Message}");
                    await Task.Delay(1000);
                }
            }

            throw new Exception("PostgreSQL did not start within the allocated timeout.");
        }

            private async Task EnsureDatabaseExistsAsync(int port)
        {
            // Connect to the default 'postgres' database to manage other databases
            var adminConnectionString = $"Host=localhost;Port={port};Username=myuser;Password=mypassword;Database=postgres;";

            try
            {
                await using var connection = new NpgsqlConnection(adminConnectionString);
                await connection.OpenAsync();
                var checkDbCmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = 'test_db';", connection);
                var exists = await checkDbCmd.ExecuteScalarAsync() != null;

                if (!exists)
                {
                    _logger.LogInformation("Database 'test_db' does not exist. Creating database...");
                    var createDbCmd = new NpgsqlCommand("CREATE DATABASE test_db;", connection);
                    await createDbCmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Database 'test_db' created successfully.");
                }
                else
                {
                    _logger.LogInformation("Database 'test_db' already exists.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error ensuring database exists: {ex.Message}");
                throw;
            }
        }

        private async Task SetupDatabaseAsync(int port)
        {
            _logger.LogInformation($"Attempting to connect to PostgreSQL with user 'myuser'.");

            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogInformation("Successfully connected to PostgreSQL.");

                // Optional setup queries if needed, e.g., setting up schemas or permissions
                var setupQuery = @"
                    GRANT ALL PRIVILEGES ON DATABASE test_db TO myuser;
                    ALTER USER myuser CREATEDB;";

                await using (var cmd = new NpgsqlCommand(setupQuery, connection))
                {
                    await cmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Database setup completed successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while setting up the database: {ex.Message}");
                throw;
            }
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
            await _minioHelper.StopContainer();
            Factory?.Dispose();
            Client?.Dispose();
        }
    }

    [CollectionDefinition("Integration Tests")]
    public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture> { }
}
