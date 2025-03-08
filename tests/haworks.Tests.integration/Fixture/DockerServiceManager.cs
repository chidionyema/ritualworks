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
using Microsoft.AspNetCore.Http;
using Respawn;

namespace Haworks.Tests
{
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
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly int _hostPort;
    
     public MinioService(IConfiguration config, ILoggerFactory loggerFactory)
    : base(config, loggerFactory, "MinioImage", "MinioContainer", "minio/minio:latest")
{
    _accessKey = config["MinIO:AccessKey"] ?? throw new ArgumentNullException("MinIO:AccessKey is required");
    _secretKey = config["MinIO:SecretKey"] ?? throw new ArgumentNullException("MinIO:SecretKey is required");
    _hostPort = config.GetValue<int>("Docker:MinioPort");
}
    
    public string AccessKey => _accessKey;
    public string SecretKey => _secretKey;
    public int Port => _hostPort;

    public override async Task StartAsync()
    {
        _logger.LogInformation("Starting MinIO with credentials: AccessKey={AccessKey}, Port={Port}", 
            _accessKey, _hostPort);
            
        await _helper.StartContainer(
            new ContainerParameters{
                HostPort = _hostPort,
                ContainerPort = 9000,
                Command = new List<string> { "server", "/data" },
                EnvVars = new List<string>
                {
                    $"MINIO_ROOT_USER={_accessKey}",
                    $"MINIO_ROOT_PASSWORD={_secretKey}"
                },
                HealthCheck = HealthCheckConfig.Minio()
            });
    }

    public override async Task WaitForReadyAsync()
    {
        var client = CreateClient();

        await client.ListBucketsAsync();
        await EnsureBucketsExistAsync(client);
        
        _logger.LogInformation("MinIO is ready with endpoint localhost:{Port}, AccessKey={AccessKey}", 
            _hostPort, _accessKey);
    }
    
    public IMinioClient CreateClient()
    {
        return new MinioClient()
            .WithEndpoint($"localhost:{_hostPort}")
            .WithCredentials(_accessKey, _secretKey)
            .WithSSL(false)
            .Build();
    }


    private async Task EnsureBucketsExistAsync(IMinioClient client)
{
    var requiredBuckets = new[] { "temp-chunks", "final-content" };
    
    foreach (var bucket in requiredBuckets)
    {
        try {
            var exists = await client.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucket));
            
            if (!exists)
            {
                _logger.LogInformation("Creating bucket: {BucketName}", bucket);
                await client.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(bucket));
                
                // Set a public read policy (for testing only)
                var policy = $@"{{
                    ""Version"": ""2012-10-17"",
                    ""Statement"": [
                        {{
                            ""Effect"": ""Allow"",
                            ""Principal"": {{""AWS"": [""*""]}},
                            ""Action"": [""s3:GetObject"", ""s3:PutObject"", ""s3:DeleteObject""],
                            ""Resource"": [""arn:aws:s3:::{bucket}/*""]
                        }}
                    ]
                }}";
                
                await client.SetPolicyAsync(
                    new SetPolicyArgs()
                        .WithBucket(bucket)
                        .WithPolicy(policy));
                
                _logger.LogInformation("Created bucket with public access: {BucketName}", bucket);
            }
            else {
                _logger.LogInformation("Bucket already exists: {BucketName}", bucket);
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error ensuring bucket exists: {BucketName}", bucket);
            throw;
        }
    }
 }
}
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

        private static async Task CreateDatabase(NpgsqlConnection connection, ILogger logger)
        {
            var user = new NpgsqlConnectionStringBuilder(connection.ConnectionString).Username;
            logger.LogInformation("Creating test database owned by {User}", user);
            await new NpgsqlCommand($"CREATE DATABASE test_db OWNER \"{user}\"", connection)
                .ExecuteNonQueryAsync();
        }

          private static async Task<bool> CheckDatabaseExists(NpgsqlConnection connection)
        {
            var cmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = 'test_db'", connection);
            return await cmd.ExecuteScalarAsync() != null;
        }


    private static async Task GrantPrivileges(NpgsqlConnection connection, ILogger logger)
    {
        logger.LogInformation("Configuring database privileges");
        var user = new NpgsqlConnectionStringBuilder(connection.ConnectionString).Username;
        var query = $@"
            ALTER USER ""{user}"" CREATEDB;
            ALTER USER ""{user}"" WITH SUPERUSER;"; // Grant superuser for testing ONLY
        await new NpgsqlCommand(query, connection).ExecuteNonQueryAsync();
    }
    
    public static async Task ResetAsync(string connectionString, ILogger logger)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    // Get tables EXCLUDING role-related tables
    var tables = await GetNonRoleTables(connection);
    
    var truncateSql = $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;";
    await new NpgsqlCommand(truncateSql, connection).ExecuteNonQueryAsync();
}

    private static async Task<List<string>> GetNonRoleTables(NpgsqlConnection connection)
    {
        var tables = new List<string>();
        var cmd = new NpgsqlCommand(
            @"SELECT table_name 
            FROM information_schema.tables 
            WHERE table_schema = 'public' 
            AND table_name NOT IN ('AspNetRoles', 'AspNetRoleClaims')", 
            connection);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add($"\"{reader.GetString(0)}\"");
        }
        return tables;
    }
  
  }
    
    
}
