using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Azure.Storage.Blobs;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using RitualWorks.Db;
using RitualWorks.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RitualWorks.Contracts;
using RitualWorks.Repositories;
using RitualWorks.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using Microsoft.Extensions.Http;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stripe;
using RitualWorks.Repositories.RitualWorks.Repositories;
using MassTransit;
using Amazon.S3;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add environment variables before appsettings.json
        builder.Configuration
            .AddEnvironmentVariables()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        // Configure services and other configurations
        await ConfigureServicesAsync(builder);

        var app = builder.Build();

        await ConfigurePipeline(app);

        app.Run();
    }

    public static async Task ConfigureServicesAsync(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<DbCredentialsService>();
    builder.Services.AddHostedService<DbCredentialsService>(sp => sp.GetRequiredService<DbCredentialsService>());

        // Configure BlobSettings
        builder.Services.Configure<BlobSettings>(builder.Configuration.GetSection("AzureBlobStorage"));
        // Configure VaultSettings
        builder.Services.Configure<VaultSettings>(builder.Configuration.GetSection("Vault"));

        // Register VaultService with HttpClient support
        builder.Services.AddHttpClient<VaultService>((sp, client) =>
        {
            var vaultSettings = sp.GetRequiredService<IOptions<VaultSettings>>().Value;
            client.BaseAddress = new Uri(vaultSettings.VaultAddress);
        });

        // Register VaultService
        builder.Services.AddSingleton(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(VaultService));

            var vaultToken = Environment.GetEnvironmentVariable("VAULT_ROOT_TOKEN");
            if (string.IsNullOrEmpty(vaultToken))
            {
                throw new InvalidOperationException("Vault token not found.");
            }
            Console.WriteLine($"Using Vault Token: {vaultToken}");
            return new VaultService(httpClient, sp.GetRequiredService<ILogger<VaultService>>(), vaultToken);
        });

        // Build a temporary service provider to fetch secrets
        var tempProvider = builder.Services.BuildServiceProvider();
        var vaultService = tempProvider.GetRequiredService<VaultService>();

        // Fetch PostgreSQL vault user credentials dynamically
        (string vaultUsername, string vaultPassword) = await vaultService.FetchPostgresCredentialsAsync("vault");

        // Fetch other secrets from Vault
        try
        {
            var secrets = await vaultService.FetchSecretsAsync(
                $"secret/{builder.Environment.EnvironmentName}",
                "jwt_key", "minio_access_key", "minio_secret_key", "rabbitmq_password"
            );

            // Inject PostgreSQL vault credentials and other secrets into configuration
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Jwt:Key"] = secrets["jwt_key"],
                ["MassTransit:RabbitMq:Password"] = secrets["rabbitmq_password"],
                ["MinIO:AccessKey"] = secrets["minio_access_key"],
                ["MinIO:SecretKey"] = secrets["minio_secret_key"]
            });

            // Set up the connection string using the vault-provided username and password
            var postgresConnectionString = $"Host=postgres_primary;Port=5432;Database=your_postgres_db;Username={vaultUsername};Password={vaultPassword}";
            builder.Configuration["ConnectionStrings:DefaultConnection"] = postgresConnectionString;

            // Logging the configuration values
            var loggerFactory = builder.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<Program>();

            logger.LogInformation("Secrets from Vault:");
            logger.LogInformation("Jwt Key: {JwtKey}", builder.Configuration["Jwt:Key"]);
            logger.LogInformation("Postgres Vault Username: {VaultUsername}", vaultUsername);
            logger.LogInformation("RabbitMQ Password: {RabbitMqPassword}", builder.Configuration["MassTransit:RabbitMq:Password"]);
            logger.LogInformation("MinIO Access Key: {MinioAccessKey}", builder.Configuration["MinIO:AccessKey"]);
            logger.LogInformation("MinIO Secret Key: {MinioSecretKey}", builder.Configuration["MinIO:SecretKey"]);

        }
        catch (HttpRequestException ex)
        {
            var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred while fetching secrets from Vault.");
            throw;
        }

        // Add PostgreSQL DbContext using dynamically configured Vault user credentials
         builder.Services.AddDbContext<RitualWorksContext>(options =>
    {
        var dbCredentialsService = builder.Services.BuildServiceProvider().GetRequiredService<DbCredentialsService>();
        var connectionString = $"Host=postgres_primary;Port=5432;Database=your_postgres_db;Username={dbCredentialsService.Username};Password={dbCredentialsService.Password}";

        var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Connecting to PostgreSQL with connection string: {ConnectionString}", connectionString);

        options.UseNpgsql(connectionString);
    });

        // Add MassTransit with RabbitMQ and log connection details
        builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.AddConsumer<MyConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                var rabbitMqHost = builder.Configuration["MassTransit:RabbitMq:Host"];
                var rabbitMqUsername = builder.Configuration["MassTransit:RabbitMq:Username"];
                var rabbitMqPassword = builder.Configuration["MassTransit:RabbitMq:Password"];

                var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Connecting to RabbitMQ Host: {Host}, Username: {Username}", rabbitMqHost, rabbitMqUsername);

                cfg.Host(new Uri(rabbitMqHost), h =>
                {
                    h.Username(rabbitMqUsername);
                    h.Password(rabbitMqPassword);
                    h.Heartbeat(10); // Heartbeat interval
                    h.RequestedConnectionTimeout(TimeSpan.FromSeconds(30)); // Connection timeout
                });

                cfg.PrefetchCount = 16;
                cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
                cfg.UseInMemoryOutbox();

                cfg.ConfigureEndpoints(context);
            });
        });

        // Add other services to the container
        builder.Services.AddControllers();

        // Log the configuration values to verify they are being read
        var jwtIssuer = builder.Configuration["Jwt:Issuer"];
        var jwtAudience = builder.Configuration["Jwt:Audience"];
        var jwtKey = builder.Configuration["Jwt:Key"];

        if (string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience) || string.IsNullOrEmpty(jwtKey))
        {
            throw new ArgumentNullException("JWT settings are not configured properly.");
        }

        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        // Add Identity
        builder.Services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<RitualWorksContext>()
            .AddDefaultTokenProviders();

        // Configure JWT Authentication
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

        // Configure Stripe
        builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
        StripeConfiguration.ApiKey = builder.Configuration.GetSection("Stripe")["SecretKey"];

        // Add repository and service dependencies
        builder.Services.AddScoped<IRitualRepository, RitualRepository>();
        builder.Services.AddScoped<IFileStorageService, LocalFileSystemService>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<IRitualService, RitualService>();
        builder.Services.AddScoped<IPostRepository, PostRepository>();
        builder.Services.AddScoped<ICommentRepository, CommentRepository>();
        builder.Services.AddScoped<OrderService>();
        builder.Services.AddScoped<ISignedUrlService, SignedUrlService>();

        // Register BlobServiceClient
        builder.Services.AddSingleton(x =>
        {
            var blobSettings = x.GetRequiredService<IOptions<BlobSettings>>().Value;
            return new BlobServiceClient(blobSettings.ConnectionString);
        });

        // Register Amazon S3 client
        builder.Services.AddSingleton<IAmazonS3>(x =>
        {
            var configuration = x.GetRequiredService<IConfiguration>();
            var awsOptions = configuration.GetAWSOptions();
            return awsOptions.CreateServiceClient<IAmazonS3>();
        });

        // Add AutoMapper
        builder.Services.AddAutoMapper(cfg =>
        {
            cfg.AddMaps(AppDomain.CurrentDomain.GetAssemblies());
        });

        // Add Swagger services
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "RitualWorks API", Version = "v1" });
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

    public static async Task ConfigurePipeline(WebApplication app)
    {
        // Standard middleware setup
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseCors("Allow3001");
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "RitualWorks API V1");
            c.RoutePrefix = "swagger";
        });

        app.MapControllers();

        await Task.CompletedTask;
    }
}
