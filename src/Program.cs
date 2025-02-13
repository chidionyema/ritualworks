using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Azure.Storage.Blobs;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using haworks.Db;
using haworks.Services;
using haworks.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using haworks.Contracts;
using haworks.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using Stripe;
using MassTransit;
using Minio;
using haworks.Models;
using Serilog;
using Serilog.Events;
using Microsoft.AspNetCore.Identity;
using System.Security.Authentication;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using StackExchange.Redis;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using System.Threading.RateLimiting;
using System.Threading;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using System.Net.Security;
using System.IO;
using Haworks.Services;
using Stripe.Checkout;
using System.Threading.Tasks;
public partial class Program
{
    public static async Task Main(string[] args)
    {
        ConfigureLogging();

        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog();

        ConfigureEnvironmentVariables(builder);

        await ConfigureServicesAsync(builder);

        var app = builder.Build();
        await ConfigurePipeline(app);

        app.Run();
    }

    private static void ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("/logs/app-log-.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }

    private static void ConfigureEnvironmentVariables(WebApplicationBuilder builder)
    {
        builder.Configuration
            .AddEnvironmentVariables()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    }

    public static async Task ConfigureServicesAsync(WebApplicationBuilder builder)
    {
        builder.Services.AddAutoMapper(typeof(MappingProfile));
        builder.Services.AddMemoryCache();

        ConfigureRateLimiting(builder);
        
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            // Use the null-forgiving operator since we assume the connection string is set.
            var redisConnectionString = builder.Configuration.GetConnectionString("Redis")!;
            var configurationOptions = ConfigurationOptions.Parse(redisConnectionString);

            // Ensure no duplicate port assignments
            configurationOptions.EndPoints.Clear();
            var redisHost = "redis-master";
            var redisPort = configurationOptions.Ssl ? 6380 : 6379;
            configurationOptions.EndPoints.Add(redisHost, redisPort);

            // Debugging
            Console.WriteLine("Configured Redis EndPoints:");
            foreach (var endpoint in configurationOptions.EndPoints)
            {
                Console.WriteLine(endpoint.ToString());
            }

            // SSL/TLS Handling
            if (configurationOptions.Ssl)
            {
                var redisTlsConfig = builder.Configuration.GetSection("RedisTls");
                if (redisTlsConfig.Exists())
                {
                    var certPath = redisTlsConfig["CertificatePath"];
                    var keyPath = redisTlsConfig["PrivateKeyPath"];
                    configurationOptions.CertificateSelection += (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) =>
                    {
                        // Add null-forgiving operators to indicate these values aren’t null.
                        var clientCert = System.IO.File.ReadAllText(certPath!);
                        var privateKey = System.IO.File.ReadAllText(keyPath!);
                        return X509Certificate2.CreateFromPem(clientCert, privateKey);
                    };
                }
            }

            options.ConfigurationOptions = configurationOptions;
        });

        var redis = ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!);
        builder.Services.AddDataProtection()
            .PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys")
            .SetApplicationName("HAWorks"); 

        builder.Services.AddSingleton<PaymentIntentService>();
        builder.Services.AddSingleton<SessionService>(provider =>
        {
            return new SessionService();
        });

        builder.Services.AddSingleton<DynamicCredentialsConnectionInterceptor>();

        // Add Identity services
        builder.Services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<haworksContext>()
            .AddDefaultTokenProviders();

        ConfigureDbContext(builder);
        ConfigureServices(builder);
        /*
        AddMassTransit(builder);
        AddMinioClient(builder);
        */
        AddSwagger(builder);

        // Added to remove CS1998 warning – ensuring an await is present.
        await Task.CompletedTask;
    }

    private static void ConfigureRateLimiting(WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: "Global",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromSeconds(60),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 5
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsync("Rate limit exceeded.", cancellationToken);
            };
        });
    }
    
    private static void ConfigureDbContext(WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<haworksContext>((serviceProvider, options) =>
        {
            var vault = serviceProvider.GetRequiredService<VaultService>();
            options.UseNpgsql(vault.GetDatabaseConnectionString().Result)
                .AddInterceptors(new DynamicCredentialsConnectionInterceptor(vault));
        }); 
    }

    private static void ConfigureServices(WebApplicationBuilder builder)
    { 
         builder.Services.AddControllers();
        ConfigureJwtAuthentication(builder);
        ConfigureStripe(builder);
        AddRepositoryAndServiceDependencies(builder);
    }

    private static void ConfigureJwtAuthentication(WebApplicationBuilder builder)
    {
        var jwtKey = builder.Configuration["Jwt:Key"];
        var jwtIssuer = builder.Configuration["Jwt:Issuer"];
        var jwtAudience = builder.Configuration["Jwt:Audience"];

        if (string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience) || string.IsNullOrEmpty(jwtKey))
        {
            throw new ArgumentNullException("JWT settings are not configured properly.");
        }

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            var keyBytes = Encoding.UTF8.GetBytes(jwtKey);
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
            };
        });
    }

    private static void ConfigureStripe(WebApplicationBuilder builder)
    {
        builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
        // Use the null-forgiving operator to signal that "SecretKey" is expected to be non-null.
        StripeConfiguration.ApiKey = builder.Configuration.GetSection("Stripe")["SecretKey"]!;
    }

    private static void AddRepositoryAndServiceDependencies(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
        builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<PaymentSessionStrategy>();
        builder.Services.AddScoped<SubscriptionSessionStrategy>();
        builder.Services.AddScoped<StripeWebhookService>();

        // Register domain services
        builder.Services.AddScoped<IPaymentProcessingService, PaymentProcessingService>();
        builder.Services.AddScoped<ISubscriptionProcessingService, SubscriptionProcessingService>();

        builder.Services.AddScoped<StripeWebhookService>();

        // builder.Services.AddScoped<IContentService, ContentService>();
        // builder.Services.AddScoped<IContentRepository, ContentRepository>();

        // Add these lines to your services configuration
        builder.Services.AddSingleton<ITelemetryService, ConsoleTelemetryService>();    
        builder.Services.AddSingleton<IVaultService, VaultService>();
    }

    private static void AddMassTransit(WebApplicationBuilder builder)
    {
        builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            x.UsingRabbitMq((context, cfg) =>
            {
                // Use null-forgiving operators on configuration values that must be set.
                var rabbitMqHost = builder.Configuration["MassTransit:RabbitMq:Host"]!;
                var rabbitMqUsername = builder.Configuration["MassTransit:RabbitMq:Username"]!;
                var rabbitMqPassword = builder.Configuration["MassTransit:RabbitMq:Password"]!;

                var rabbitMqSslEnabled = bool.Parse(builder.Configuration["MassTransit:RabbitMq:Ssl:Enabled"]!);
                var rabbitMqServerName = builder.Configuration["MassTransit:RabbitMq:Ssl:ServerName"];

                var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Connecting to RabbitMQ Host: {Host}, Username: {Username}, SSL: {SslEnabled}", rabbitMqHost, rabbitMqUsername, rabbitMqSslEnabled);

                cfg.Host(new Uri(rabbitMqHost), h =>
                {
                    h.Username(rabbitMqUsername);
                    h.Password(rabbitMqPassword);
                    h.Heartbeat(10);
                    h.RequestedConnectionTimeout(TimeSpan.FromSeconds(30));

                    if (rabbitMqSslEnabled)
                    {
                        h.UseSsl(ssl =>
                        {
                            ssl.Protocol = SslProtocols.Tls12;
                            ssl.ServerName = rabbitMqServerName;
                        });
                    }
                });

                cfg.PrefetchCount = 16;
                cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
                // Updated to use the recommended overload.
                cfg.UseInMemoryOutbox(context);

                cfg.ConfigureEndpoints(context);
            });
        });
    }

    private static void AddMinioClient(WebApplicationBuilder builder)
    {
        var minioConfig = builder.Configuration.GetSection("MinIO").Get<MinioSettings>();
        builder.Services.AddSingleton(sp =>
        {
            return new MinioClient()
                .WithEndpoint(minioConfig.Endpoint)
                .WithCredentials(minioConfig.AccessKey, minioConfig.SecretKey)
                .WithSSL(minioConfig.Secure)
                .Build();
        });
    }

    private static void AddSwagger(WebApplicationBuilder builder)
    {
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "haworks API", Version = "v1" });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "Please enter JWT with Bearer into field",
                Name = "Authorization",
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    new string[] { }
                }
            });
        });
    }

    public static async Task ConfigurePipeline(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseCors("Allow3001");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "haworks API V1");
            c.RoutePrefix = "swagger";
        });

        app.MapControllers();
        await Task.CompletedTask;
    }
}
