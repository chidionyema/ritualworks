using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using haworks.Db;
using Polly;
using Polly.Retry;
using Xunit;
using Microsoft.AspNetCore.TestHost;
using Haworks.Services;
using System.Net;
using System.Net.Http;

namespace Haworks.Tests
{
    // A simple pass-through delegating handler.
    public class PassThroughHandler : DelegatingHandler
    {
        public PassThroughHandler(HttpMessageHandler innerHandler)
        {
            InnerHandler = innerHandler;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return base.SendAsync(request, cancellationToken);
        }
    }

    public class IntegrationTestFixture : IAsyncLifetime
    {
        private readonly DockerHelper _postgresHelper;
        private readonly ILogger<DockerHelper> _logger;
        private const int DockerPostgresPort = 5433;
        private readonly string _connectionString;
        private readonly AsyncRetryPolicy _retryPolicy;

        public IntegrationTestFixture()
        {
            // Load configuration from appsettings.json in the test project.
            var configuration = new ConfigurationBuilder()
                .SetBasePath(GetTestConfigPath())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var dbHost = configuration["Database:Host"] ?? "localhost";
            var dbPort = DockerPostgresPort.ToString();
            var dbName = configuration["Database:Name"] ?? "test_db";
            var dbUser = configuration["Database:User"] ?? "myuser";
            var dbPassword = configuration["Database:Password"] ?? "mypassword";

            _connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword};";

            var loggerFactory = LoggerFactory.Create(builder => { builder.AddConsole(); });
            _logger = loggerFactory.CreateLogger<DockerHelper>();

            _retryPolicy = Policy.Handle<NpgsqlException>()
                .WaitAndRetryAsync(
                    retryCount: 5,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    onRetry: (exception, delay, attempt, context) =>
                    {
                        _logger.LogWarning($"Retry {attempt} due to {exception.Message}. Waiting {delay.TotalSeconds} seconds...");
                    });

            _postgresHelper = new DockerHelper(
                _logger,
                imageName: configuration["Docker:PostgresImage"] ?? "postgres:13",
                containerName: configuration["Docker:PostgresContainerName"] ?? "postgres_test_container",
                postStartCallback: async (port) =>
                {
                    await EnsureDatabaseExistsAsync(port);
                    await SetupDatabaseAsync(port);
                }
            );
        }

        // Shared CookieContainer for tests.
        public CookieContainer CookieContainer { get; } = new CookieContainer();

        // Create an HttpClient using a handler that uses the shared CookieContainer.
        public HttpClient CreateClientWithCookies()
        {
            var httpClientHandler = new HttpClientHandler
            {
                CookieContainer = this.CookieContainer
            };
            var delegatingHandler = new PassThroughHandler(httpClientHandler);
            return CreateFactory().CreateDefaultClient(delegatingHandler);
        }

        public async Task InitializeAsync()
        {
            await _postgresHelper.StartContainer(
                hostPort: int.Parse(Configuration["Docker:PostgresHostPort"] ?? DockerPostgresPort.ToString()),
                containerPort: 5432,
                environmentVariables: new List<string>
                {
                    $"POSTGRES_USER={Configuration["Database:User"] ?? "myuser"}",
                    $"POSTGRES_PASSWORD={Configuration["Database:Password"] ?? "mypassword"}"
                }
            );
        }

        public async Task DisposeAsync()
        {
            try
            {
                await _postgresHelper.StopContainer();
                await _postgresHelper.RemoveContainer();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during cleanup: {ex.Message}");
            }
        }

        private async Task WaitForPostgresAsync(string connectionString, int maxRetries = 10, int delayMilliseconds = 2000)
        {
            var retries = 0;
            while (retries < maxRetries)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync();
                    _logger.LogInformation("Successfully connected to PostgreSQL.");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"PostgreSQL not ready (attempt {retries + 1}/{maxRetries}): {ex.Message}");
                    await Task.Delay(delayMilliseconds);
                    retries++;
                }
            }
            throw new Exception("Failed to connect to PostgreSQL after multiple retries.");
        }

        private async Task EnsureDatabaseExistsAsync(int port)
        {
            var dbUser = Configuration["Database:User"] ?? "myuser";
            var dbPassword = Configuration["Database:Password"] ?? "mypassword";
            var adminConnectionString = $"Host=localhost;Port={port};Username={dbUser};Password={dbPassword};Database=postgres;";
            _logger.LogInformation($"Admin Connection String: {adminConnectionString}");
            await WaitForPostgresAsync(adminConnectionString);
            await _retryPolicy.ExecuteAsync(async () =>
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
            });
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

        // Only truncate RefreshTokens (to preserve registered users).
        public async Task ResetDatabaseAsync()
        {
            _logger.LogInformation("Resetting database by truncating RefreshTokens...");
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                var truncateQuery = @"TRUNCATE TABLE ""RefreshTokens"" CASCADE;";
                await using (var cmd = new NpgsqlCommand(truncateQuery, connection))
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                _logger.LogInformation("Database reset completed by truncating RefreshTokens.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error resetting database: {ex.Message}");
                throw;
            }
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

        public WebApplicationFactory<Program> CreateFactory()
        {
            return new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    // Set environment to "Test" so that cookie logic sets Secure = false.
                    builder.UseEnvironment("Test");
                     builder.ConfigureAppConfiguration(config => 
                {
                    config.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        ["Jwt:Key"] = Configuration["Jwt:Key"],
                        ["Jwt:Issuer"] = Configuration["Jwt:Issuer"],
                        ["Jwt:Audience"] = Configuration["Jwt:Audience"]
                    });
                });

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
                        var connectionString = _connectionString;
                        services.AddEntityFrameworkNpgsql()
                            .AddDbContext<haworksContext>(options =>
                                options.UseNpgsql(connectionString)
                                       .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

                        // Register authentication schemes: default "Test" and dummy "Microsoft".
                        services.AddAuthentication(options =>
                        {
                            options.DefaultScheme = "Test";
                        })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Microsoft", options => { });

                        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
                        services.AddSingleton<VaultService, FakeVaultService>();
                        services.AddSingleton<IVaultService, FakeVaultService>();

                        var sp = services.BuildServiceProvider();
                        using (var scope = sp.CreateScope())
                        {
                            var db = scope.ServiceProvider.GetRequiredService<haworksContext>();
                            db.Database.EnsureCreated();
                            ResetDatabaseAsync().Wait();
                            SeedData(scope.ServiceProvider).Wait();
                        }
                    });

                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton<IStartupFilter>(new StartupFilter());
                    });
                });
        }

        private async Task SeedData(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<haworksContext>();
            // Option: Do not seed any fixed user to avoid duplicate registration issues.
            // Seed a category for completeness.
            var category = new Category(Guid.NewGuid(), "Test Category");
            context.Categories.Add(category);
            await context.SaveChangesAsync();
        }
    }

    [CollectionDefinition("Integration Tests", DisableParallelization = true)]
    public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
    {
    }
}
