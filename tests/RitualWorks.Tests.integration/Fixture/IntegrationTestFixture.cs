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
        private readonly ILogger<DockerHelper> _logger;

        // Connection string built from configuration
        private readonly string _connectionString;

        public IntegrationTestFixture()
        {
            // Load configuration from appsettings.json in the test project
            var configuration = new ConfigurationBuilder()
                .SetBasePath(GetTestConfigPath())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var dbHost = configuration["Database:Host"] ?? "localhost"; // Use config or default
            var dbPort = configuration["Database:Port"] ?? "5432";
            var dbName = configuration["Database:Name"] ?? "test_db";
            var dbUser = configuration["Database:User"] ?? "myuser";
            var dbPassword = configuration["Database:Password"] ?? "mypassword";

            _connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword};";

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });
            _logger = loggerFactory.CreateLogger<DockerHelper>();

            _postgresHelper = new DockerHelper(
                _logger,
                imageName: configuration["Docker:PostgresImage"] ?? "postgres:13", // Load image name from config
                containerName: configuration["Docker:PostgresContainerName"] ?? "postgres_test_container", // Load container name from config
                postStartCallback: async (port) =>
                {
                    await EnsureDatabaseExistsAsync(port);
                    await SetupDatabaseAsync(port);
                }
            );
        }

        public async Task InitializeAsync()
        {
            // Start PostgreSQL container
            await _postgresHelper.StartContainer(
                hostPort: int.Parse(Configuration["Docker:PostgresHostPort"] ?? "5432"), // Load ports from config
                containerPort: 5432,
                environmentVariables: new List<string> {
                    $"POSTGRES_USER={Configuration["Database:User"] ?? "myuser"}",
                    $"POSTGRES_PASSWORD={Configuration["Database:Password"] ?? "mypassword"}"
                });
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
                        // Use a unique database name per test class - NOTE: While we generate a unique name, we are still connecting
                        // to the same 'test_db' instance within Docker, just using a different logical name within EF Core.
                        // For true isolation, we would need to dynamically create and drop databases in Docker, which adds complexity.
                        // For now, we are using table truncation in ResetDatabaseAsync for isolation within the 'test_db'.
                        var uniqueDbName = "test_db_" + Guid.NewGuid(); // Logical unique name -  Not fully isolated DB in Docker without more complex setup.
                        var connectionString = _connectionString; // Re-use the base connection string - connects to the 'test_db'

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
                        services.AddSingleton<IStartupFilter>(new StartupFilter()); // TODO: Clarify the purpose of StartupFilter if still needed.
                    });
                });
        }

        public async Task DisposeAsync()
        {
            try
            {
                await _postgresHelper.StopContainer();
                await _postgresHelper.RemoveContainer(); // Ensure container is removed on DisposeAsync for cleanup
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during cleanup: {ex.Message}");
            }
        }

        private async Task EnsureDatabaseExistsAsync(int port)
        {
            var adminConnectionString = $"Host=localhost;Port={port};Username=postgres;Password=mypassword;Database=postgres;"; // Connect to 'postgres' DB for admin tasks

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
            _logger.LogInformation($"Attempting to connect to PostgreSQL with user '{Configuration["Database:User"] ?? "myuser"}'.");

            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogInformation("Successfully connected to PostgreSQL.");

                var setupQuery = $@"
                    GRANT ALL PRIVILEGES ON DATABASE test_db TO ""{Configuration["Database:User"] ?? "myuser"}"";
                    ALTER USER ""{Configuration["Database:User"] ?? "myuser"}"" CREATEDB;";

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


        public async Task ResetDatabaseAsync()
        {
            _logger.LogInformation("Resetting database by truncating tables...");
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                // Truncate tables to reset database - Adjust table names if needed to match your schema
                var truncateQuery = @"
                    TRUNCATE TABLE ""AspNetUsers"" CASCADE;
                    TRUNCATE TABLE ""RefreshTokens"" CASCADE;
                    -- Add other tables you want to truncate for test isolation if necessary
                ";

                await using (var cmd = new NpgsqlCommand(truncateQuery, connection))
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                _logger.LogInformation("Database reset completed by truncating tables.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error resetting database: {ex.Message}");
                throw; // Re-throw to fail test setup if database reset fails
            }
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

        // Access Configuration for test settings
        public IConfiguration Configuration
        {
            get
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(GetTestConfigPath())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                return builder.Build();
            }
        }
    }

    [CollectionDefinition("Integration Tests", DisableParallelization = true)]
    public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
    {
    }
}