// Program.cs

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Azure.Storage.Blobs;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using haworks.Db;
using haworks.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using haworks.Contracts;
using haworks.Repositories;
using haworks.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;
using Stripe;
using MassTransit;
using Minio;
using haworks.Models;
using Serilog;
using Serilog.Events;
using Microsoft.AspNetCore.Identity;
using System.Security.Authentication;

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

        // Add Identity services
        builder.Services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<haworksContext>()
            .AddDefaultTokenProviders();

        // Register the connection string provider and the interceptor
        builder.Services.AddSingleton<IConnectionStringProvider, ConnectionStringProvider>();
        builder.Services.AddSingleton<DynamicCredentialsConnectionInterceptor>();
        builder.Services.AddHostedService<CredentialRefreshService>();

        ConfigureDbContext(builder);
        ConfigureServices(builder);
        AddMassTransit(builder);
        AddMinioClient(builder);
        AddSwagger(builder);
        AddCredentialRefreshService(builder);
    }

    private static void ConfigureDbContext(WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<haworksContext>((serviceProvider, options) =>
        {
            var connectionStringProvider = serviceProvider.GetRequiredService<IConnectionStringProvider>();
            var interceptor = serviceProvider.GetRequiredService<DynamicCredentialsConnectionInterceptor>();

            options.UseNpgsql(connectionStringProvider.GetConnectionString());
            options.AddInterceptors(interceptor);
        });
    }

    private static void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddControllers();
        ConfigureBlobService(builder);
        ConfigureJwtAuthentication(builder);
        ConfigureStripe(builder);
        AddRepositoryAndServiceDependencies(builder);
    }

    private static void ConfigureBlobService(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(x =>
        {
            var blobSettings = x.GetRequiredService<IOptions<BlobSettings>>().Value;
            return new BlobServiceClient(blobSettings.ConnectionString);
        });
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
        StripeConfiguration.ApiKey = builder.Configuration.GetSection("Stripe")["SecretKey"];
    }

    private static void AddRepositoryAndServiceDependencies(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<OrderService>();
        builder.Services.AddScoped<IAssetService, AssetService>();
    }

    private static void AddMassTransit(WebApplicationBuilder builder)
    {
        builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            // x.AddConsumer<MyConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                var rabbitMqHost = builder.Configuration["MassTransit:RabbitMq:Host"];
                var rabbitMqUsername = builder.Configuration["MassTransit:RabbitMq:Username"];
                var rabbitMqPassword = builder.Configuration["MassTransit:RabbitMq:Password"];

                var rabbitMqSslEnabled = bool.Parse(builder.Configuration["MassTransit:RabbitMq:Ssl:Enabled"]);
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
                cfg.UseInMemoryOutbox();

                cfg.ConfigureEndpoints(context);
            });
        });
    }

    private static void AddMinioClient(WebApplicationBuilder builder)
    {
        // Register MinioClient as a singleton
        var minioConfig = builder.Configuration.GetSection("MinIO").Get<MinioSettings>();
        builder.Services.AddSingleton<MinioClient>(sp =>
        {
            return (MinioClient)new MinioClient()
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
            c.AddSecurityRequirement(new OpenApiSecurityRequirement {
                {
                    new OpenApiSecurityScheme {
                        Reference = new OpenApiReference {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    new string[] { }
                }
            });
        });
    }

    private static void AddCredentialRefreshService(WebApplicationBuilder builder)
    {
        builder.Services.AddHostedService<CredentialRefreshService>();
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
