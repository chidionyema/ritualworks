using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
using StackExchange.Redis;

namespace Haworks.Tests
{
    public class IntegrationTestFixture : IAsyncLifetime
    {
        private readonly DockerHelper _postgresHelper;
        private readonly DockerHelper _redisHelper;
        private readonly ILogger<DockerHelper> _logger;
        private const int DockerPostgresPort = 5433;
        private const int DockerRedisPort = 6380;
        private readonly string _connectionString;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly IConfiguration _configuration;

        // Health-check values are defined as long (in nanoseconds)
        public HealthConfig PostgresHealthCheck { get; }
        public HealthConfig RedisHealthCheck { get; }

        public IntegrationTestFixture()
        {
            // Load configuration from appsettings.json in the test project.
            var configuration = new ConfigurationBuilder()
                .SetBasePath(GetTestConfigPath())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _configuration = configuration;

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

           PostgresHealthCheck = new HealthConfig
        {
            Test = new List<string> { "CMD-SHELL", $"pg_isready -U {configuration["Database:User"] ?? "myuser"}" },
            Interval = TimeSpan.FromSeconds(2),
            Timeout = TimeSpan.FromSeconds(1),
            Retries = 5,
            StartPeriod = 0L
        };

        RedisHealthCheck = new HealthConfig
        {
            Test = new List<string> { "CMD-SHELL", "redis-cli ping | grep PONG" },
            Interval = TimeSpan.FromSeconds(2),
            Timeout = TimeSpan.FromSeconds(5),
            Retries = 5,
            StartPeriod = TimeSpan.FromSeconds(10).Ticks
        };


            // Initialize Postgres helper.
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

            // Initialize Redis helper.
            _redisHelper = new DockerHelper(
                _logger,
                imageName: configuration["Docker:RedisImage"] ?? "redis:latest",
                containerName: configuration["Docker:RedisContainerName"] ?? "redis_test_container",
                postStartCallback: async (port) =>
                {
                    // Build the connection string for Redis using the host port.
                    var redisConnectionString = $"localhost:{port},abortConnect=false";
                    await WaitForRedisAsync(redisConnectionString);
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
            // Start Postgres container.
            await _postgresHelper.StartContainer(
                hostPort: int.Parse(Configuration["Docker:PostgresHostPort"] ?? DockerPostgresPort.ToString()),
                containerPort: 5432,
                environmentVariables: new List<string>
                {
                    $"POSTGRES_USER={Configuration["Database:User"] ?? "myuser"}",
                    $"POSTGRES_PASSWORD={Configuration["Database:Password"] ?? "mypassword"}"
                },
                command: new List<string>(),         // Pass an empty command list.
                healthCheck: PostgresHealthCheck       // Pass the health-check.
            );

            // Start Redis container.
           var RedisHostPort = int.Parse(_configuration["Docker:RedisHostPort"] ?? DockerRedisPort.ToString());
            await _redisHelper.StartContainer(
                hostPort: RedisHostPort,
                containerPort: 6379,
                environmentVariables: new List<string>(),
                command: new List<string>(),
                healthCheck: RedisHealthCheck
            );
        }

        public async Task DisposeAsync()
        {
            try
            {
                await _postgresHelper.StopContainer();
                await _postgresHelper.RemoveContainer();

                await _redisHelper.StopContainer();
                await _redisHelper.RemoveContainer();
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
    private async Task WaitForRedisAsync(string connectionString, int maxRetries = 10, int delayMilliseconds = 2000, TimeSpan timeout = default)
    {
        timeout = timeout == default? TimeSpan.FromSeconds(30): timeout;
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;

        using var cts = new CancellationTokenSource(timeout);

        for (int retries = 0; retries < maxRetries; retries++)
        {
            try
            {
                using var redis = await ConnectionMultiplexer.ConnectAsync(options); // Correct usage
                if (redis.IsConnected)
                {
                    _logger.LogInformation("Successfully connected to Redis.");
                    return;
                }
                else
                {
                    _logger.LogWarning("Redis is not connected, retrying...");
                }
            }
            catch (RedisConnectionException ex)
            {
                _logger.LogWarning($"Redis connection failed (attempt {retries + 1}/{maxRetries}): {ex.Message}");
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
            {
                _logger.LogError($"Redis connection timed out after {timeout.TotalSeconds} seconds.");
                throw new Exception($"Failed to connect to Redis after multiple retries within the timeout of {timeout.TotalSeconds} seconds.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Redis connection failed (attempt {retries + 1}/{maxRetries}): {ex.Message}");
            }

            await Task.Delay(delayMilliseconds);
        }

        throw new Exception("Failed to connect to Redis after multiple retries.");
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
                            ["Jwt:Audience"] = Configuration["Jwt:Audience"],
                            ["Redis:ConnectionString"] = $"localhost:{DockerRedisPort},abortConnect=false"
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

                         services.AddSingleton<IConnectionMultiplexer>(sp =>
                        {
                            var config = sp.GetRequiredService<IConfiguration>();
                            // Ensure your configuration includes the correct key; here we assume "Redis:ConnectionString"
                            var redisConnectionString = config["Redis:ConnectionString"] ?? $"localhost:{DockerRedisPort},abortConnect=false";
                            var options = ConfigurationOptions.Parse(redisConnectionString);
                            options.AbortOnConnectFail = false;
                            return ConnectionMultiplexer.Connect(options);
    });

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
