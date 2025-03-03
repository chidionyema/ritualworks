using haworks.Contracts;
using haworks.Db;
using haworks.Services;
using Haworks.Infrastructure;
using Haworks.Infrastructure.Data;
using Haworks.Infrastructure.Repositories;
using haworks.Webhooks;
using MassTransit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;
using Stripe;
using Stripe.Checkout;
using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Linq;
using haworks.Infrastructure;
namespace haworks.Extensions
{
    public static class InfrastructureExtensions
    {
        /// <summary>
        /// Registers all core infrastructure services.
        /// </summary>
        /// <remarks>
        /// IMPORTANT: We NO LONGER build a partial container or call vault.InitializeAsync() here.
        /// Instead, Vault is fully initialized in Program.cs AFTER the final container is built.
        /// </remarks>
        public static IServiceCollection AddInfrastructureServices(
            this IServiceCollection services,
            IConfiguration config,
            ILogger logger)
        {
            // 1. Register Vault (but do NOT initialize!)
            services.AddVaultServices(config);

            // 2. Register DB contexts & Identity
            services.AddDatabaseServices(config);

            // 3. Redis caching, data protection, distributed locking
            //    (made synchronous, but we could do an async helper or inline .GetAwaiter().GetResult())
            services.AddRedisAndDataProtection(config, logger).GetAwaiter().GetResult();

            // 4. MassTransit (RabbitMQ), Minio, Stripe, antivirus, etc.
            services
               // .AddMassTransit(config)
                 .AddMinioStorage(config)
                .AddStripeServices(config)
                .AddSingleton<IVirusScanner, ClamAVScanner>()
                .AddSingleton<IFileSignatureValidator, FileSignatureValidator>();

            return services;
        }

        #region Vault Services

        private static IServiceCollection AddVaultServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton<IVaultService, VaultService>()
                    .AddSingleton<DynamicCredentialsConnectionInterceptor>();

            services.AddOptions<VaultOptions>()
                    .Bind(config.GetSection("Vault"))
                    .ValidateDataAnnotations();

            services.AddHostedService<VaultCredentialRenewalService>();

            // Example health checks
            services.AddHealthChecks()
                .AddCheck<VaultHealthCheck>("vault")
                .AddCheck<DatabaseHealthCheck>("Database");

            return services;
        }

        #endregion

        #region Database Contexts and Repositories

        private static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddDbContext<ProductContext>((sp, options) =>
            {
                // We'll get the vault but won't call InitializeAsync here.
                var vault = sp.GetRequiredService<IVaultService>();
                var connectionString = vault.GetDatabaseConnectionStringAsync().GetAwaiter().GetResult();
                options.UseNpgsql(connectionString)
                       .AddInterceptors(sp.GetRequiredService<DynamicCredentialsConnectionInterceptor>());
            });

            services.AddDbContext<OrderContext>((sp, options) =>
            {
                var vault = sp.GetRequiredService<IVaultService>();
                var connectionString = vault.GetDatabaseConnectionStringAsync().GetAwaiter().GetResult();
                options.UseNpgsql(connectionString)
                       .AddInterceptors(sp.GetRequiredService<DynamicCredentialsConnectionInterceptor>());
            });

            services.AddDbContext<ContentContext>((sp, options) =>
            {
                var vault = sp.GetRequiredService<IVaultService>();
                var connectionString = vault.GetDatabaseConnectionStringAsync().GetAwaiter().GetResult();
                options.UseNpgsql(connectionString)
                       .AddInterceptors(sp.GetRequiredService<DynamicCredentialsConnectionInterceptor>());
            });

            services.AddDbContext<IdentityContext>((sp, options) =>
            {
                var vault = sp.GetRequiredService<IVaultService>();
                var connectionString = vault.GetDatabaseConnectionStringAsync().GetAwaiter().GetResult();
                options.UseNpgsql(connectionString)
                       .AddInterceptors(sp.GetRequiredService<DynamicCredentialsConnectionInterceptor>());
            });

            services.AddIdentity<User, IdentityRole>()
                .AddEntityFrameworkStores<IdentityContext>()
                .AddDefaultTokenProviders();
            
            // Register repositories
            services.AddScoped<IProductContextRepository, ProductContextRepository>();
            services.AddScoped<IContentContextRepository, ContentContextRepository>();
            services.AddScoped<IOrderContextRepository, OrderContextRepository>();
            services.AddScoped<IIdentityContextRepository, IdentityContextRepository>();

            return services;
        }

        #endregion

        #region Redis, Data Protection, and Distributed Locking

       #region Redis, Data Protection, and Distributed Locking

    private static async Task<IServiceCollection> AddRedisAndDataProtection(
        this IServiceCollection services,
        IConfiguration config,
        ILogger logger)
    {
        logger.LogInformation("[Redis] Starting Redis configuration...");
        
        var connectionString = config.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogCritical("[Redis] Redis connection string is missing in configuration");
            throw new ArgumentNullException(nameof(connectionString), "The Redis connection string is missing from the configuration.");
        }

        logger.LogInformation("[Redis] Using connection string: {ConnectionString}", connectionString);
        
        // Parse the connection string into Redis configuration options.
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false; 
        logger.LogDebug("[Redis] Parsed configuration options: {@Options}", options);

        if (options.Ssl)
        {
            logger.LogInformation("[Redis] SSL enabled - configuring certificates...");
            var certificatePath = config["Redis:CertificatePath"];
            var privateKeyPath = config["Redis:PrivateKeyPath"];
            
            if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(privateKeyPath))
            {
                logger.LogError("[Redis] SSL enabled but missing certificate paths. CertPath: {CertPath}, KeyPath: {KeyPath}", 
                    certificatePath, privateKeyPath);
                throw new InvalidOperationException("SSL is enabled for Redis, but either the certificate or private key path is missing.");
            }

            logger.LogInformation("[Redis] Using certificate: {CertificatePath} with key: {PrivateKeyPath}", 
                certificatePath, privateKeyPath);
            
            options.CertificateSelection += (_, _, _, _, _) =>
            {
                logger.LogDebug("[Redis] Creating certificate from PEM files");
                return X509Certificate2.CreateFromPemFile(certificatePath, privateKeyPath);
            };
        }

        logger.LogInformation("[Redis] Attempting to connect to Redis...");
        try
        {
            var redis = await ConnectionMultiplexer.ConnectAsync(options);
            
            logger.LogInformation("[Redis] Successfully connected to Redis. Configuration: {Configuration}", 
                redis.Configuration);
            
            logger.LogDebug("[Redis] Adding Redis services to DI container");
            services
                .AddSingleton<IConnectionMultiplexer>(redis)
                .AddStackExchangeRedisCache(o =>
                {
                    o.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis);
                })
                .AddDataProtection()
                    .PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys");


                    // Register RedisDistributedLockProvider as a singleton
            services.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();


            services.AddSingleton<IDistributedLockFactory>(sp =>
            {
                logger.LogDebug("[Redis] Creating distributed lock factory");
                var multiplexers = new List<RedLockMultiplexer>
                {
                    new RedLockMultiplexer(sp.GetRequiredService<IConnectionMultiplexer>())
                };
                return RedLockFactory.Create(multiplexers);
            });

            logger.LogInformation("[Redis] Redis services registered successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Redis] Failed to connect to Redis. Endpoints: {Endpoints}", 
                string.Join(", ", options.EndPoints));
            throw new InvalidOperationException("Redis connection failed", ex);
        }

        return services;
    }

    #endregion

        #endregion

        #region MassTransit, Minio, and Stripe

        private static IServiceCollection AddMassTransit(this IServiceCollection services, IConfiguration config)
        {
            services.AddMassTransit(x =>
            {
                x.SetKebabCaseEndpointNameFormatter();
                x.UsingRabbitMq((ctx, cfg) =>
                {
                    var rmq = config.GetSection("MassTransit:RabbitMq");
                    cfg.Host(rmq["Host"], h =>
                    {
                        h.Username(rmq["Username"] ?? string.Empty);
                        h.Password(rmq["Password"] ?? string.Empty);
                        if (rmq.GetValue<bool>("Ssl:Enabled"))
                        {
                            h.UseSsl(s =>
                            {
                                s.Protocol = SslProtocols.Tls12;
                                s.ServerName = rmq["Ssl:ServerName"];
                            });
                        }
                    });
                    cfg.ConfigureEndpoints(ctx);
                });
            });
            return services;
        }

        private static IServiceCollection AddMinioStorage(this IServiceCollection services, IConfiguration config)
        {
            var minioConfig = config.GetSection("MinIO");
            
            // Add MinioClient as singleton
            services.AddSingleton<IMinioClient>(_ =>
                new MinioClient()
                    .WithEndpoint(minioConfig["Endpoint"])
                    .WithCredentials(minioConfig["AccessKey"], minioConfig["SecretKey"])
                    .WithSSL(minioConfig.GetValue<bool>("Secure"))
                    .Build());
            
            // Add ContentStorageService as singleton
            services.AddSingleton<IContentStorageService, ContentStorageService>(sp =>
                new ContentStorageService(
                    sp.GetRequiredService<IMinioClient>(),
                    sp.GetRequiredService<ILogger<ContentStorageService>>(),
                    config));

            return services;
        }

        private static IServiceCollection AddStripeServices(this IServiceCollection services, IConfiguration config)
        {
            StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
            services
                .AddSingleton<PaymentIntentService>()
                .AddSingleton<SessionService>(provider => new SessionService())
                .AddScoped<StripeWebhookService>();

            return services;
        }

        #endregion
    }
}
