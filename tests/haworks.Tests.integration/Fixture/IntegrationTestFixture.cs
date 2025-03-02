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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Minio;
using Minio.Exceptions;
using Minio.DataModel.Args;
using Npgsql;
using Polly;
using Polly.Retry;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;
using Haworks.Infrastructure.Data;
using haworks.Contracts;
using haworks.Db;
using haworks.Services;

namespace Haworks.Tests
{
    #region Core Test Infrastructure
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _testConfigPath;
        private readonly IConfigurationRoot _testConfiguration;

        public CustomWebApplicationFactory(string testConfigPath)
        {
            _testConfigPath = testConfigPath;
            _testConfiguration = ConfigurationLoader.LoadTestConfiguration(testConfigPath);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging
                .ClearProviders()
                .AddConsole()
                .AddDebug()
                .SetMinimumLevel(LogLevel.Debug));

            ConfigureAuthentication(builder);
            ConfigureEnvironmentAndConfiguration(builder);
            ConfigureTestServices(builder);
            //Apply migrations AFTER test services are configured
            builder.ConfigureServices(services => 
            {
                var serviceProvider = services.BuildServiceProvider();
                using var scope = serviceProvider.CreateScope();
                var sp = scope.ServiceProvider;
                
                // Apply migrations for IdentityContext
                var identityContext = sp.GetRequiredService<IdentityContext>();
                identityContext.Database.Migrate();
                
                // Apply migrations for ContentContext
                var contentContext = sp.GetRequiredService<ContentContext>();
                contentContext.Database.Migrate();
            });
        }

        private void ConfigureAuthentication(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.AddTransient<TestAuthMiddleware>();
                services.ConfigureJwtBearer(_testConfiguration);
            });
        }

        private void ConfigureEnvironmentAndConfiguration(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test")
                   .UseContentRoot(_testConfigPath)
                   .ConfigureAppConfiguration(config => 
                       config.AddConfiguration(_testConfiguration));
        }

        private void ConfigureTestServices(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveSwaggerGenerator()
                        .ConfigureTestAuthorization()
                        .ReplaceService<IVaultService, FakeVaultService>(sp => new FakeVaultService())
                        .ReplaceService<IVirusScanner, FakeVirusScanner>(sp => new FakeVirusScanner())
                        .ConfigureContentStorage(_testConfiguration)
                        .ConfigureDatabases(_testConfiguration)
                        .ConfigureRedis(_testConfiguration);
            });
        }
    }

    public class IntegrationTestFixture : IAsyncLifetime
    {
        private readonly DockerServiceManager _dockerManager;
        private readonly ILogger<IntegrationTestFixture> _logger;
        
        public CookieContainer CookieContainer { get; } = new CookieContainer();
        public CustomWebApplicationFactory Factory { get; }
        public IConfiguration Configuration { get; }

        public IntegrationTestFixture()
        {
            Configuration = ConfigurationLoader.LoadTestConfiguration(PathHelper.TestConfigPath);

            // Create an ILoggerFactory
            ILoggerFactory loggerFactoryForFixture = LoggerFactory.Create(builder => builder.AddConsole());
            // Use the LoggerFactory to create an ILogger for IntegrationTestFixture
            _logger = loggerFactoryForFixture.CreateLogger<IntegrationTestFixture>();

            // Create logger for DockerHelper -  You were also doing it wrong here!
            ILoggerFactory loggerFactoryForDocker = LoggerFactory.Create(builder => builder.AddConsole());
            var dockerLogger = loggerFactoryForDocker.CreateLogger<Haworks.Tests.DockerHelper>();

            _dockerManager = new DockerServiceManager(Configuration, loggerFactoryForDocker);
            Factory = new CustomWebApplicationFactory(PathHelper.TestConfigPath);
       }

        public async Task InitializeAsync() => await _dockerManager.StartAllServicesAsync();
        public async Task DisposeAsync() => await _dockerManager.StopAllServicesAsync();

        public HttpClient CreateClientWithCookies() => 
            HttpClientFactory.CreateWithCookies(Factory, CookieContainer);

       
        public async Task ResetDatabaseAsync()
        {
            // Reset SQL database
            await DatabaseMaintainer.ResetAsync(
                Configuration.GetConnectionString("DefaultConnection"), 
                _logger
            );

            // Get Redis connection string from configuration
            var redisConfig = ConfigurationOptions.Parse(Configuration.GetConnectionString("Redis"));
    
            // Reset Redis databases
            var config = new ConfigurationOptions
            {
                EndPoints = { redisConfig.EndPoints[0] }, // Use first endpoint without parameters
                AllowAdmin = true,
                AbortOnConnectFail = redisConfig.AbortOnConnectFail // Preserve connection behavior
            };
            
            var redis = await ConnectionMultiplexer.ConnectAsync(config);
            var server = redis.GetServer(redisConfig.EndPoints[0]);
            await server.FlushAllDatabasesAsync();
        }

        public HttpClient CreateAuthorizedClient(string token) => 
            HttpClientFactory.CreateAuthorized(Factory, token);

        public HttpClient CreateBypassAuthClient() => 
            HttpClientFactory.CreateBypassAuth(Factory);
    }
    #endregion

    #region Configuration Extensions
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection RemoveSwaggerGenerator(this IServiceCollection services)
        {
            services.RemoveAll<ISwaggerProvider>();
            return services;
        }
        

        public static IServiceCollection ConfigureTestAuthorization(this IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();

                options.AddPolicy(AuthorizationPolicies.ContentUploader, policy => 
                    policy.RequireRole(UserRoles.ContentUploader));
            });
            return services;
        }

        public static IServiceCollection ReplaceService<TService, TImplementation>(
        this IServiceCollection services, 
        Func<IServiceProvider, TImplementation> implementationFactory) 
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        services.AddSingleton<TService, TImplementation>(implementationFactory); 
        return services;
    }

        public static IServiceCollection ConfigureContentStorage(
        this IServiceCollection services, IConfiguration config)
        {
            return services.ReplaceService<IContentStorageService, ContentStorageService>(sp =>
                new ContentStorageService(
                    new MinioClient()
                        .WithEndpoint(config["MinIO:Endpoint"] ?? "localhost:9000")
                        .WithSSL(false)
                        .WithCredentials(
                            config["MinIO:AccessKey"] ?? "minioadmin",
                            config["MinIO:SecretKey"] ?? "minioadmin")
                        .Build(),
                    sp.GetRequiredService<ILogger<ContentStorageService>>(),
                    config));
        }

        public static IServiceCollection ConfigureDatabases(
            this IServiceCollection services, IConfiguration config)
        {
            var connectionString = config.GetConnectionString("DefaultConnection");
            
            services.AddDbContext<IdentityContext>(options => 
                options.ConfigureNpgsql(connectionString));
            
            services.AddDbContext<ContentContext>(options => 
                options.ConfigureNpgsql(connectionString));
            
            services.AddDbContext<ProductContext>(options => 
                options.ConfigureNpgsql(connectionString));

            return services;
        }

        public static IServiceCollection ConfigureRedis(
            this IServiceCollection services, IConfiguration config)
        {
            return services.AddSingleton<IConnectionMultiplexer>(sp => 
                ConnectionMultiplexer.Connect(
                    ConfigurationOptions.Parse(config.GetConnectionString("Redis"))));
        }

        public static void ConfigureNpgsql(
            this DbContextOptionsBuilder options, string connectionString)
        {
            options.UseNpgsql(connectionString)
                   .ConfigureWarnings(warnings => 
                       warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        }

        public static void ConfigureJwtBearer(
            this IServiceCollection services, IConfiguration config)
        {
            services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                options => ConfigureTokenValidation(options, config));
        }

        private static void ConfigureTokenValidation(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions options, 
            IConfiguration config)
        {
            options.SaveToken = true;
            options.IncludeErrorDetails = true;
            options.TokenValidationParameters = CreateValidationParameters(config);
        }

        private static TokenValidationParameters CreateValidationParameters(IConfiguration config)
        {
            var key = Convert.FromBase64String(config["Jwt:Key"]);
            return new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = config["Jwt:Audience"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier
            };
        }
    }
    #endregion

    #region HttpClient Factories
    public static class HttpClientFactory
    {
                // In HttpClientFactory.cs
        public static HttpClient CreateWithCookies(CustomWebApplicationFactory factory, CookieContainer container)
        {
            var handler = new HttpClientHandler { 
                CookieContainer = container,
                UseCookies = true,
                AllowAutoRedirect = false  // Add this
            };
            
            // Create client with this handler
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
                BaseAddress = new Uri("http://localhost")
            });
            
            // Return client configured with this handler
            return client;
        }


        public static HttpClient CreateAuthorized(
            CustomWebApplicationFactory factory, string token)
        {
            return factory.WithWebHostBuilder(builder => 
                builder.ConfigureServices(services => 
                    services.PostConfigure<AuthorizationOptions>(options => 
                        options.AddPolicy(AuthorizationPolicies.ContentUploader, policy => 
                            policy.RequireAuthenticatedUser()
                                  .RequireRole(UserRoles.ContentUploader)))))
                .CreateClient()
                .WithBearerToken(token);
        }

        public static HttpClient CreateBypassAuth(CustomWebApplicationFactory factory)
        {
            return factory.WithWebHostBuilder(builder => 
                builder.ConfigureServices(services => 
                    services.AddSingleton<IAuthorizationHandler, AllowAllHandler>()))
                .CreateClient();
        }

        private static HttpClient WithBearerToken(this HttpClient client, string token)
        {
            client.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return client;
        }
    }
    #endregion

    #region Docker Management
    public class DockerServiceManager
    {
        private readonly List<DockerService> _services;
        private readonly ILogger _logger;

        public DockerServiceManager(IConfiguration config, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<DockerServiceManager>();
            _services = new List<DockerService>
            {
                new PostgresService(config, loggerFactory),
                new RedisService(config, loggerFactory),
                new MinioService(config, loggerFactory)
            };
        }

        public async Task StartAllServicesAsync()
        {
            foreach (var service in _services)
            {
                await service.StartAsync();
                await service.WaitForReadyAsync();
            }
        }

        public async Task StopAllServicesAsync()
        {
            foreach (var service in _services.AsEnumerable().Reverse())
            {
                await service.StopAsync();
            }
        }
    }

    public abstract class DockerService
    {
        protected readonly IConfiguration _config;
        protected readonly ILogger _logger;
        protected readonly DockerHelper _helper;

        protected DockerService(
            IConfiguration config,
            ILoggerFactory loggerFactory, // Accept ILoggerFactory instead of ILogger
            string imageKey,
            string containerKey,
            string defaultImage)
        {
            _config = config;
            _logger = loggerFactory.CreateLogger(GetType()); // Create logger for the service
            var dockerLogger = loggerFactory.CreateLogger<DockerHelper>(); // Create DockerHelper logger
            _helper = new DockerHelper(
                dockerLogger,
                _config[$"Docker:{imageKey}"] ?? defaultImage,
                _config[$"Docker:{containerKey}"] ?? $"{imageKey}_test");
        }

        public abstract Task StartAsync();
        public abstract Task WaitForReadyAsync();
        public Task StopAsync() => _helper.StopContainer();
    }

    public class PostgresService : DockerService
    {
        public PostgresService(IConfiguration config, ILoggerFactory loggerFactory) 
            : base(config, loggerFactory, "PostgresImage", "PostgresContainer", "postgres:13") { }

        // In PostgresService.cs
        public override async Task StartAsync()
        {
            await _helper.StartContainer(new ContainerParameters 
            {
                HostPort = _config.GetValue<int>("Docker:PostgresPort", 5433),
                ContainerPort = 5432,
                EnvVars = new List<string>
                {
                    $"POSTGRES_USER={_config["Database:User"]}",
                    $"POSTGRES_PASSWORD={_config["Database:Password"]}"
                },
                HealthCheck = HealthCheckConfig.Postgres(_config["Database:User"] ?? "postgres")
            });
        }

        public override async Task WaitForReadyAsync()
        {
            var connectionString = $"Host=localhost;Port={_helper.HostPort};" +
                $"Username={_config["Database:User"]};Password={_config["Database:Password"]};";
            
            await DatabaseMaintainer.EnsureCreatedAsync(connectionString, _logger);
        }
    }

    public class RedisService : DockerService
    {
        public RedisService(IConfiguration config, ILoggerFactory loggerFactory)
            : base(config, loggerFactory, "RedisImage", "RedisContainer", "redis:latest") { }

        public override async Task StartAsync()
        {
            await _helper.StartContainer(
                new ContainerParameters{
                    HostPort = _config.GetValue<int>("Docker:RedisPort", 6380),
                    ContainerPort = 6379,
                    HealthCheck = HealthCheckConfig.Redis()}
            );
        }

        public override async Task WaitForReadyAsync()
        {
            await ConnectionMultiplexer.ConnectAsync(
                $"localhost:{_helper.HostPort},abortConnect=false");
        }
    }

    public class MinioService : DockerService
    {
        public MinioService(IConfiguration config, ILoggerFactory loggerFactory)
            : base(config, loggerFactory, "MinioImage", "MinioContainer", "minio/minio:latest") { }

      public override async Task StartAsync()
      {
        await _helper.StartContainer(
        new ContainerParameters{ 
        HostPort = _config.GetValue<int>("Docker:MinioPort", 9001),
        ContainerPort = 9000,
        Command = new List<string> { "server", "/data" },
        EnvVars = new List<string>
        {
            $"MINIO_ROOT_USER={_config["MinIO:AccessKey"]}",
            $"MINIO_ROOT_PASSWORD={_config["MinIO:SecretKey"]}"
        },
        HealthCheck = HealthCheckConfig.Minio()});

     }

         public override async Task WaitForReadyAsync()
    {
        var client = new MinioClient()
            .WithEndpoint($"localhost:{_helper.HostPort}")
            .WithCredentials(
                _config["MinIO:AccessKey"] ?? "minioadmin",
                _config["MinIO:SecretKey"] ?? "minioadmin")
            .Build();

        await client.ListBucketsAsync();
        await EnsureBucketsExistAsync(client); // This calls the method
    }


    private async Task EnsureBucketsExistAsync(IMinioClient client)
        {
            var requiredBuckets = new[] { "temp-chunks", "final-content" };
            
            foreach (var bucket in requiredBuckets)
            {
                var exists = await client.BucketExistsAsync(
                    new BucketExistsArgs().WithBucket(bucket));
                
                if (!exists)
                {
                    await client.MakeBucketAsync(
                        new MakeBucketArgs().WithBucket(bucket));
                    _logger.LogInformation("Created bucket: {BucketName}", bucket);
                }
            }
        }
}
    #endregion

    #region Support Classes
    public static class AuthorizationPolicies
    {
        public const string ContentUploader = "ContentUploader";
    }

    public static class UserRoles
    {
        public const string ContentUploader = "ContentUploader";
    }

    public static class HealthCheckConfig
    {
        public static HealthConfig Postgres(string user) => new HealthConfig
        {
            Test = new List<string> { "CMD-SHELL", $"pg_isready -U {user}" },
            Interval = TimeSpan.FromSeconds(2),
            Timeout = TimeSpan.FromSeconds(1),
            Retries = 20
        };

        public static HealthConfig Redis() => new HealthConfig
        {
            Test = new List<string> { "CMD-SHELL", "redis-cli ping | grep PONG" },
            Interval = TimeSpan.FromSeconds(2),
            Timeout = TimeSpan.FromSeconds(5),
            Retries = 20
        };

        public static HealthConfig Minio() => new HealthConfig
        {
            Test = new List<string> { "CMD-SHELL", "mc admin health check" },
            Interval = TimeSpan.FromSeconds(2),
            Timeout = TimeSpan.FromSeconds(5),
            Retries = 20
        };
    }

    public static class DatabaseMaintainer
    {
        private static readonly AsyncRetryPolicy _retryPolicy = Policy
            .Handle<NpgsqlException>()
            .WaitAndRetryAsync(5, attempt => 
                TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        public static async Task EnsureCreatedAsync(string connectionString, ILogger logger)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                
                var exists = await CheckDatabaseExists(connection);
                if (!exists) await CreateDatabase(connection, logger);
                
                await GrantPrivileges(connection, logger);
            });
        }

        private static async Task<bool> CheckDatabaseExists(NpgsqlConnection connection)
        {
            var cmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = 'test_db'", connection);
            return await cmd.ExecuteScalarAsync() != null;
        }

        private static async Task CreateDatabase(NpgsqlConnection connection, ILogger logger)
        {
            logger.LogInformation("Creating test database");
            await new NpgsqlCommand("CREATE DATABASE test_db", connection)
                .ExecuteNonQueryAsync();
        }

        private static async Task GrantPrivileges(NpgsqlConnection connection, ILogger logger)
        {
            logger.LogInformation("Configuring database privileges");
            var user = connection.ConnectionString.Split("Username=")[1].Split(';')[0];
            var query = $@"
                GRANT ALL PRIVILEGES ON DATABASE test_db TO ""{user}"";
                ALTER USER ""{user}"" CREATEDB;";
            
            await new NpgsqlCommand(query, connection).ExecuteNonQueryAsync();
        }

        public static async Task ResetAsync(string connectionString, ILogger logger)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await new NpgsqlCommand(@"TRUNCATE TABLE ""Contents"" CASCADE", connection)
                .ExecuteNonQueryAsync();
            logger.LogInformation("Database reset completed");
        }
    }

    public static class PathHelper
    {
        public static string TestConfigPath => FindParentContaining("appsettings.Test.json");

        private static string FindParentContaining(string fileName)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !directory.GetFiles(fileName).Any())
            {
                directory = directory.Parent;
            }
            return directory?.FullName ?? throw new FileNotFoundException(fileName);
        }
    }

    public static class ConfigurationLoader
    {
        public static IConfigurationRoot LoadTestConfiguration(string path) => 
            new ConfigurationBuilder()
                .SetBasePath(path)
                .AddJsonFile("appsettings.Test.json")
                .Build();
    }

    public class AllowAllHandler : IAuthorizationHandler
    {
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            foreach (var requirement in context.PendingRequirements)
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }


    [CollectionDefinition("Integration Tests", DisableParallelization = true)]
    public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture> { }
    #endregion
}
