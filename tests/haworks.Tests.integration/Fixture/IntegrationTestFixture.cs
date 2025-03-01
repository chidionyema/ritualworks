using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using Swashbuckle.AspNetCore.Swagger;

using System.Net.Http;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Haworks.Infrastructure.Data;
using Npgsql;
using haworks.Db;
using Polly;
using Polly.Retry;
using Xunit;
using haworks.Services;
using StackExchange.Redis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Minio;
using Minio.Exceptions;
using Minio.DataModel.Args;
using haworks.Contracts;
using Microsoft.Extensions.Configuration;
namespace Haworks.Tests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _testConfigPath;
        private readonly IConfigurationRoot _testConfiguration; // Store configuration

        public CustomWebApplicationFactory(string testConfigPath)
        {
            _testConfigPath = testConfigPath;
            _testConfiguration = LoadTestConfiguration(testConfigPath); // Load once in constructor
        }

        private static IConfigurationRoot LoadTestConfiguration(string testConfigPath)
        {
            return new ConfigurationBuilder()
                .SetBasePath(testConfigPath)
                .AddJsonFile("appsettings.Test.json", optional: false, reloadOnChange: true)
                .Build();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Force the environment to "Test"
            builder.UseEnvironment("Test");

            builder.ConfigureAppConfiguration((context, config) =>
            {
                // Clear existing configuration sources.
                config.Sources.Clear();


                // Set the base path to the test config folder and load the test configuration file.
                config.SetBasePath(_testConfigPath);
                config.AddJsonFile("appsettings.Test.json", optional: false, reloadOnChange: true);

                // Optionally add additional in-memory configuration overrides.
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    // For testing, you might use a different key—but ensure it’s a valid Base-64 string.
                    // Here, for demonstration, we use a simple (but valid) Base‑64 string.
                    // ["Jwt:Key"] = "VGhpcyBpcyBhIFRlc3QgS2V5IQ==", // "This is a Test Key!" in Base-64
                    // ["Jwt:Issuer"] = "TestIssuer",
                    //["Jwt:Audience"] = "TestAudience",
                    // Removed hardcoded Redis connection string here, will be read from config
                });
            });
            builder.UseContentRoot(_testConfigPath);

            // Override services for testing.
            builder.ConfigureTestServices(services =>
            {
                var swaggerService = services.FirstOrDefault(s => s.ServiceType == typeof(ISwaggerProvider));
                if (swaggerService != null)
                {
                    services.Remove(swaggerService);
                }
                // Replace Vault with a fake implementation - consider if this is still needed.
                services.RemoveAll<IVaultService>();
                services.AddSingleton<IVaultService, FakeVaultService>(); // Or use real Vault if testing Vault integration

                // Replace ContentStorageService with real MinIO implementation
                services.RemoveAll<IContentStorageService>();


                // Load the test configuration.


                services.AddSingleton<IContentStorageService>(sp =>
                {
                    var minioEndpoint = _testConfiguration["MinIO:Endpoint"] ?? "localhost:9000";
                    var minioAccessKey = _testConfiguration["MinIO:AccessKey"] ?? "minioadmin";
                    var minioSecretKey = _testConfiguration["MinIO:SecretKey"] ?? "minioadmin";
                    var logger = sp.GetRequiredService<ILogger<ContentStorageService>>();

                    // Correct MinioClient instantiation using builder pattern
                    var minioClient = new MinioClient()
                        .WithEndpoint(minioEndpoint)
                        .WithCredentials(minioAccessKey, minioSecretKey)
                        .Build();

                    return new ContentStorageService(minioClient, logger, _testConfiguration); // Instantiate ContentStorageService with MinioClient - Ensure constructor expects MinioClient
                });


                // Register a test authentication scheme.
            //     services.AddAuthentication(options =>
              //  {
                  //  options.DefaultScheme = "Test";
              ////  })
               // .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
              ////  .AddCookie("Cookies", options =>
             //   {
                 //   options.Cookie.Name = "jwt";
                  //  options.TicketDataFormat = new TestJwtDataFormat(
                  //      Configuration["Jwt:Key"] ?? "YourTestKeyHereAtLeast32CharactersLong"
                  //  );
                 //   options.DataProtectionProvider = new TestDataProtector();
               // });

                // Add authorization policy configuration
                services.AddAuthorization(options =>
                {
                    options.AddPolicy("ContentUploader", policy =>  policy
                    .RequireRole("ContentUploader")
                    .RequireClaim("permission", "upload_content"));
                });

                // Register your EF Core context using the test connection string.
                var testConnection = _testConfiguration.GetConnectionString("DefaultConnection");
                // Adjust the context type below as needed.
                services.AddEntityFrameworkNpgsql()
                    .AddDbContext<IdentityContext>(options =>
                        options.UseNpgsql(testConnection)
                                     .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

                using (var scope = services.BuildServiceProvider().CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();
                    context.Database.Migrate();
                }

                 services.AddEntityFrameworkNpgsql()
                    .AddDbContext<ContentContext>(options =>
                        options.UseNpgsql(testConnection)
                                     .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

                using (var scope = services.BuildServiceProvider().CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ContentContext>();
                    context.Database.Migrate();
                }

                services.AddEntityFrameworkNpgsql()
                    .AddDbContext<ProductContext>(options =>
                        options.UseNpgsql(testConnection)
                                     .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

                using (var scope = services.BuildServiceProvider().CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ContentContext>();
                    context.Database.Migrate();
                }
                

                // Register Redis using the test connection string from configuration.
                services.AddSingleton<IConnectionMultiplexer>(sp =>
                {
                    var redisConnectionString = _testConfiguration.GetConnectionString("RedisConnection"); // Read from config
                    var options = ConfigurationOptions.Parse(redisConnectionString);
                    options.AbortOnConnectFail = false;
                    return ConnectionMultiplexer.Connect(options);
                });
            });
        }
    }

    public class IntegrationTestFixture : IAsyncLifetime
    {
        private readonly DockerHelper _postgresHelper;
        private readonly DockerHelper _redisHelper;
        private readonly DockerHelper _minioHelper; // Add MinIO DockerHelper
        private readonly ILogger<DockerHelper> _logger;
        private const int DockerPostgresPort = 5433;
        private const int DockerRedisPort = 6380;
        private const int DockerMinioPort = 9001; // Different from default 9000 to avoid conflicts if you run MinIO locally
        private readonly string _connectionString;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly IConfiguration _configuration; // Store configuration

        // Health-check values.
        public HealthConfig PostgresHealthCheck { get; }
        public HealthConfig RedisHealthCheck { get; }
        public HealthConfig MinioHealthCheck { get; } // Add MinIO health check

        // Shared CookieContainer for tests.
        public CookieContainer CookieContainer { get; } = new CookieContainer();

        // Our custom factory that uses the test configuration.
        public CustomWebApplicationFactory Factory { get; }

        public IntegrationTestFixture()
        {
            // Load configuration from the test folder's appsettings.json.
            _configuration = new ConfigurationBuilder() // Initialize _configuration here
                .SetBasePath(GetTestConfigPath())
                .AddJsonFile("appsettings.Test.json", optional: false, reloadOnChange: true)
                .Build();

            // Build connection string based on Docker-mapped port.
            var dbHost = _configuration["Database:Host"] ?? "localhost";
            var dbPort = DockerPostgresPort.ToString();
            var dbName = _configuration["Database:Name"] ?? "test_db";
            var dbUser = _configuration["Database:User"] ?? "myuser";
            var dbPassword = _configuration["Database:Password"] ?? "mypassword";
            _connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword};";

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<DockerHelper>();

            _retryPolicy = Policy.Handle<NpgsqlException>()
                .WaitAndRetryAsync(5, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    (exception, delay, attempt, context) =>
                    {
                        _logger.LogWarning($"Retry {attempt} due to {exception.Message}. Waiting {delay.TotalSeconds} seconds...");
                    });

            PostgresHealthCheck = new HealthConfig
            {
                Test = new List<string> { "CMD-SHELL", $"pg_isready -U {_configuration["Database:User"] ?? "myuser"}" },
                Interval = TimeSpan.FromSeconds(2),
                Timeout = TimeSpan.FromSeconds(1),
                Retries = 20,
                StartPeriod = TimeSpan.FromSeconds(10).Ticks
            };

            RedisHealthCheck = new HealthConfig
            {
                Test = new List<string> { "CMD-SHELL", "redis-cli ping | grep PONG" },
                Interval = TimeSpan.FromSeconds(2),
                Timeout = TimeSpan.FromSeconds(5),
                Retries = 20,
                StartPeriod = TimeSpan.FromSeconds(10).Ticks
            };
            MinioHealthCheck = new HealthConfig // Define MinIO health check
            {
                Test = new List<string> { "CMD-SHELL", "mc admin health check" }, // Basic MinIO health check using mc CLI
                Interval = TimeSpan.FromSeconds(2),
                Timeout = TimeSpan.FromSeconds(5),
                Retries = 20,
                StartPeriod = TimeSpan.FromSeconds(10).Ticks // Give MinIO a bit longer to start
            };


            // Initialize Docker helpers for PostgreSQL, Redis, and MinIO.
            _postgresHelper = new DockerHelper(
                _logger,
                imageName: _configuration["Docker:PostgresImage"] ?? "postgres:13",
                containerName: _configuration["Docker:PostgresContainerName"] ?? "postgres_test_container",
                postStartCallback: async port =>
                {
                    await EnsureDatabaseExistsAsync(port);
                    await SetupDatabaseAsync(port);
                });

            _redisHelper = new DockerHelper(
                _logger,
                imageName: _configuration["Docker:RedisImage"] ?? "redis:latest",
                containerName: _configuration["Docker:RedisContainerName"] ?? "redis_test_container",
                postStartCallback: async port =>
                {
                    var redisConnectionString = $"localhost:{port},abortConnect=false"; // Will be overridden by DockerHelper
                    await WaitForRedisAsync(redisConnectionString);
                });
            _minioHelper = new DockerHelper( // Initialize MinIO DockerHelper
                _logger,
                imageName: _configuration["Docker:MinioImage"] ?? "minio/minio:latest",
                containerName: _configuration["Docker:MinioContainerName"] ?? "minio_test_container",
                postStartCallback: async port =>
                {
                    var minioEndpoint = $"localhost:{port}";
                    await WaitForMinioAsync(minioEndpoint);
                    await SetupMinioAsync(minioEndpoint); // Optional: Setup buckets, users etc. if needed for tests
                });


            // Create our custom factory with the test configuration folder path.
            Factory = new CustomWebApplicationFactory(GetTestConfigPath());
        }

        public async Task InitializeAsync()
        {
            // Start PostgreSQL container.
            await _postgresHelper.StartContainer(
                hostPort: int.Parse(_configuration["Docker:PostgresHostPort"] ?? DockerPostgresPort.ToString()), // Use _configuration
                containerPort: 5432,
                environmentVariables: new List<string>
                {
                    $"POSTGRES_USER={_configuration["Database:User"] ?? "myuser"}", // Use _configuration
                    $"POSTGRES_PASSWORD={_configuration["Database:Password"] ?? "mypassword"}" // Use _configuration
                },
                command: new List<string>(),
                healthCheck: PostgresHealthCheck
            );

            // Wait for Postgres to be ready before proceeding and add a small buffer
            var postgresConnectionStringForWait = $"Host=localhost;Port={int.Parse(_configuration["Docker:PostgresHostPort"] ?? DockerPostgresPort.ToString())};Username={_configuration["Database:User"] ?? "myuser"};Password={_configuration["Database:Password"] ?? "mypassword"};Database=postgres;";
            _logger.LogInformation($"Waiting for Postgres to be ready. Connection string: {postgresConnectionStringForWait}");
            await WaitForPostgresAsync(postgresConnectionStringForWait); // Wait here
            await Task.Delay(2000); // Add a 2-second delay after Postgres is ready


            // Start Redis container.
            var redisHostPort = int.Parse(_configuration["Docker:RedisHostPort"] ?? DockerRedisPort.ToString());
            await _redisHelper.StartContainer(
                hostPort: redisHostPort,
                containerPort: 6379,
                environmentVariables: new List<string>(),
                command: new List<string>(),
                healthCheck: RedisHealthCheck
            );

            // Start MinIO container.
            var minioHostPort = int.Parse(_configuration["Docker:MinioHostPort"] ?? DockerMinioPort.ToString()); 
            await _minioHelper.StartContainer(
                hostPort: minioHostPort,
                containerPort: 9000, // MinIO default container port
                environmentVariables: new List<string>
                {
                    $"MINIO_ACCESS_KEY={_configuration["MinIO:AccessKey"] ?? "minioadmin"}", 
                    $"MINIO_SECRET_KEY={_configuration["MinIO:SecretKey"] ?? "minioadmin"}"  
                },
                command: new List<string> { "server", "/data" }, 
                healthCheck: MinioHealthCheck
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

                await _minioHelper.StopContainer(); // Stop MinIO container
                await _minioHelper.RemoveContainer(); // Remove MinIO container
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during cleanup: {ex.Message}");
            }
        }

        private async Task WaitForPostgresAsync(string connectionString, int maxRetries = 20, int delayMilliseconds = 2000) // Increased retries
        {
            int retries = 0;
            while (retries < maxRetries)
            {
                try
                {
                    _logger.LogDebug($"Attempting PostgreSQL connection. Connection string: {connectionString}, Attempt: {retries + 1}/{maxRetries}"); // Log connection string
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
            timeout = timeout == default ? TimeSpan.FromSeconds(30) : timeout;
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            using var cts = new System.Threading.CancellationTokenSource(timeout);

            for (int retries = 0; retries < maxRetries; retries++)
            {
                try
                {
                    using var redis = await ConnectionMultiplexer.ConnectAsync(options);
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
                    throw new Exception($"Failed to connect to Redis within the timeout of {timeout.TotalSeconds} seconds.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Redis connection failed (attempt {retries + 1}/{maxRetries}): {ex.Message}");
                }
                await Task.Delay(delayMilliseconds);
            }
            throw new Exception("Failed to connect to Redis after multiple retries.");
        }
       private async Task WaitForMinioAsync(string endpoint, int maxRetries = 10, int delayMilliseconds = 2000, TimeSpan timeout = default)
        {
            timeout = timeout == default ? TimeSpan.FromSeconds(30) : timeout;
            using var cts = new System.Threading.CancellationTokenSource(timeout);
            IMinioClient minioClient = new MinioClient() // Changed to IMinioClient
                .WithEndpoint(endpoint)
                .WithCredentials(_configuration["MinIO:AccessKey"] ?? "minioadmin", _configuration["MinIO:SecretKey"] ?? "minioadmin")
                .Build();

            for (int retries = 0; retries < maxRetries; retries++)
            {
                try
                {
                    await minioClient.ListBucketsAsync().ConfigureAwait(false);

                    _logger.LogInformation("Successfully connected to MinIO and listed buckets.");
                    return;
                }
                catch (MinioException ex)
                {
                    _logger.LogWarning($"MinIO connection failed (attempt {retries + 1}/{maxRetries}): {ex.Message}");
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
                {
                    _logger.LogError($"MinIO connection timed out after {timeout.TotalSeconds} seconds.");
                    throw new Exception($"Failed to connect to MinIO within the timeout of {timeout.TotalSeconds} seconds.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"MinIO connection failed (attempt {retries + 1}/{maxRetries}): {ex.Message}");
                }
                await Task.Delay(delayMilliseconds);
            }
            throw new Exception("Failed to connect to MinIO after multiple retries."); // Removed extra parenthesis here
        }

        private async Task EnsureDatabaseExistsAsync(int port)
        {
            var dbUser = _configuration["Database:User"] ?? "myuser"; // Use _configuration
            var dbPassword = _configuration["Database:Password"] ?? "mypassword"; // Use _configuration
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
        private async Task SetupMinioAsync(string endpoint)
        {
            // change 3: Use builder pattern for MinioClient constructor
            var minioClient = new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(_configuration["MinIO:AccessKey"] ?? "minioadmin", _configuration["MinIO:SecretKey"] ?? "minioadmin")
                .Build();

            string[] bucketsToCreate = new[] { "documents", "temp-chunks", "videos", "images" }; // Example buckets

            foreach (var bucketName in bucketsToCreate)
            {
                bool exists = await minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName)); // Modified BucketExistsAsync call
                if (!exists)
                {
                    _logger.LogInformation($"Bucket '{bucketName}' does not exist. Creating...");
                    await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName)); // Modified MakeBucketAsync call
                    _logger.LogInformation($"Bucket '{bucketName}' created successfully.");
                }
                else
                {
                    _logger.LogInformation($"Bucket '{bucketName}' already exists.");
                }
            }
        }


        private async Task SetupDatabaseAsync(int port)
        {
            _logger.LogInformation($"Attempting to connect to PostgreSQL with user '{_configuration["Database:User"] ?? "myuser"}'.");
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogInformation("Successfully connected to PostgreSQL.");
                // Corrected SQL query string:
                var setupQuery = $@"
                    GRANT ALL PRIVILEGES ON DATABASE test_db TO ""{(_configuration["Database:User"] ?? "myuser")}"";
                    ALTER USER ""{(_configuration["Database:User"] ?? "myuser")}"" CREATEDB;";
                await using (var cmd = new NpgsqlCommand(setupQuery, connection))
                {
                    await cmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Database setup completed successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error occurred while setting up the database: {ex.Message}");
                throw;
            }
        }

        // --- Helpers to locate project and configuration paths ---
        private string GetProjectPath()
        {
            var currentDirectory = AppContext.BaseDirectory;
            var directoryInfo = new DirectoryInfo(currentDirectory);
            while (directoryInfo != null && !File.Exists(Path.Combine(directoryInfo.FullName, "RitualWorks.sln")))
            {
                directoryInfo = directoryInfo.Parent;
            }
            if (directoryInfo == null)
                throw new DirectoryNotFoundException("Solution directory could not be found.");
            return directoryInfo.FullName;
        }

        private string GetApplicationPath()
        {
            var projectPath = GetProjectPath();
            var applicationPath = Path.Combine(projectPath, "src");
            if (!Directory.Exists(applicationPath))
                throw new DirectoryNotFoundException($"Application path '{applicationPath}' does not exist.");
            return applicationPath;
        }

        private string GetTestConfigPath()
        {
            var projectPath = GetProjectPath();
            var testConfigPath = Path.Combine(projectPath, "tests", "haworks.Tests.integration");
            if (!Directory.Exists(testConfigPath))
                throw new DirectoryNotFoundException($"Test configuration path '{testConfigPath}' does not exist.");
            return testConfigPath;
        }

        public IConfiguration Configuration // Make the property use the _configuration field
        {
            get
            {
                return _configuration;
            }
        }

        // Create an HttpClient that uses a shared CookieContainer.
        public HttpClient CreateClientWithCookies()
        {
            var httpClientHandler = new HttpClientHandler { CookieContainer = this.CookieContainer };
            return Factory.CreateDefaultClient(new PassThroughHandler(httpClientHandler));
        }

        public async Task ResetDatabaseAsync()
        {
            _logger.LogInformation("Resetting database by truncating Contents table..."); // Updated log message
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                var truncateQuery = @"TRUNCATE TABLE ""Contents"" CASCADE;"; // Truncate Contents table
                await using (var cmd = new NpgsqlCommand(truncateQuery, connection))
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                _logger.LogInformation("Database reset completed by truncating Contents table."); // Updated log message
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error resetting database: {ex.Message}");
                throw;
            }
        }
    }

    [CollectionDefinition("Integration Tests", DisableParallelization = true)]
    public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
    {
    }
}