using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Docker.DotNet;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using haworks.Db;
using Xunit;
using System.Linq;
using Microsoft.AspNetCore.TestHost;
namespace haworks.Tests
{
    public class IntegrationTestFixture : IAsyncLifetime
    {
        private readonly DockerHelper _postgresHelper;
        private readonly DockerHelper _rabbitMqHelper;
        private readonly DockerHelper _minioHelper;
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
                postStartCallback: async (port) =>
                {
                    await EnsureDatabaseExistsAsync(port);
                    await SetupDatabaseAsync(port);
                }
            );

            _rabbitMqHelper = new DockerHelper(
                _logger,
                imageName: "rabbitmq:3-management",
                containerName: "rabbitmq_test_container"
            );

            _minioHelper = new DockerHelper(
                _logger,
                imageName: "minio/minio",
                containerName: "minio_test_container"
            );
        }

        public async Task InitializeAsync()
        {
            // Start PostgreSQL container
            await _postgresHelper.StartContainer(
                hostPort: 5432,
                containerPort: 5432,
                environmentVariables: new List<string> { "POSTGRES_USER=myuser", "POSTGRES_PASSWORD=mypassword" });

            // Start RabbitMQ container
            await _rabbitMqHelper.StartContainer(
                hostPort: 5672,
                containerPort: 5672,
                environmentVariables: new List<string> { "RABBITMQ_DEFAULT_USER=guest", "RABBITMQ_DEFAULT_PASS=guest" });

            // Start MinIO container
            await _minioHelper.StartContainer(
                hostPort: 9000,
                containerPort: 9000,
                environmentVariables: new List<string> { "MINIO_ACCESS_KEY=minio", "MINIO_SECRET_KEY=minio123" });
        }

        // Method to create a new WebApplicationFactory per test class
        public WebApplicationFactory<Program> CreateFactory()
        {
            return new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    var appPath = GetApplicationPath();
                    var testPath = GetTestConfigPath();

                    builder.UseSetting("ContentRoot", appPath);
                    builder.ConfigureAppConfiguration((context, config) =>
                    {
                        config.SetBasePath(testPath);
                        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    });

                    builder.ConfigureTestServices(services =>
                    {
                        // Use a unique database name per test class
                        var uniqueDbName = "test_db_" + Guid.NewGuid();
                        var connectionString = $"Host=localhost;Port=5432;Database={uniqueDbName};Username=myuser;Password=mypassword;";

                        services.AddEntityFrameworkNpgsql()
                            .AddDbContext<haworksContext>(options =>
                                options.UseNpgsql(connectionString));

                        services.AddAuthentication("Test")
                            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });

                        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

                        var sp = services.BuildServiceProvider();

                        using (var scope = sp.CreateScope())
                        {
                            var scopedServices = scope.ServiceProvider;
                            var db = scopedServices.GetRequiredService<haworksContext>();
                            db.Database.Migrate();

                            // Seed data
                            SeedData(scopedServices).Wait();
                        }
                    });

                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton<IStartupFilter>(new StartupFilter());
                    });
                });
        }

        public async Task DisposeAsync()
        {
            try
            {
                await _postgresHelper.StopContainer();
                await _rabbitMqHelper.StopContainer();
                await _minioHelper.StopContainer();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during cleanup: {ex.Message}");
            }
        }

        private async Task EnsureDatabaseExistsAsync(int port)
        {
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
            var context = serviceProvider.GetRequiredService<haworksContext>();

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

            while (directoryInfo != null && !File.Exists(Path.Combine(directoryInfo.FullName, "haworks.sln")))
            {
                directoryInfo = directoryInfo.Parent;
            }

            if (directoryInfo == null)
            {
                throw new DirectoryNotFoundException("Solution directory could not be found.");
            }

            return directoryInfo.FullName;
        }

        private string GetApplicationPath()
        {
            var projectPath = GetProjectPath();

            var applicationPath = Path.Combine(projectPath, "src");

            if (!Directory.Exists(applicationPath))
            {
                throw new DirectoryNotFoundException($"Application path '{applicationPath}' does not exist.");
            }

            return applicationPath;
        }

        private string GetTestConfigPath()
        {
            var projectPath = GetProjectPath();

            var testConfigPath = Path.Combine(projectPath, "tests", "haworks.Tests.integration");

            if (!Directory.Exists(testConfigPath))
            {
                throw new DirectoryNotFoundException($"Test configuration path '{testConfigPath}' does not exist.");
            }

            return testConfigPath;
        }
    }

    [CollectionDefinition("Integration Tests", DisableParallelization = true)]
    public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
    {
    }
}
