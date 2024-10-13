// Program.cs
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
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stripe;
using MassTransit;
using Minio;
using RitualWorks.Models;
using Serilog;

public partial class Program
{
    public static async Task Main(string[] args)
    {  // Configure Serilog to write to a specific file path inside the container
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.information()
            .Enrich.FromLogContext()
            .WriteTo.Console() // Write to console for standard output
            .WriteTo.File("/logs/app-log-.txt", rollingInterval: RollingInterval.Day) // Write to file with daily rolling
            .CreateLogger();

        var builder = WebApplication.CreateBuilder(args);
         builder.Host.UseSerilog();
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
        builder.Services.AddSingleton<VaultService>(sp =>
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

        // Configure DatabaseCredentials
        builder.Services.Configure<DatabaseCredentials>(builder.Configuration.GetSection("DatabaseCredentials"));

        // Build a temporary service provider to fetch secrets
        var tempProvider = builder.Services.BuildServiceProvider();
        var vaultService = tempProvider.GetRequiredService<VaultService>();

        // Fetch PostgreSQL vault user credentials dynamically
        DatabaseCredentials dbCredentials = await vaultService.FetchPostgresCredentialsAsync("vault");

        // Inject database credentials into configuration
        builder.Configuration["DatabaseCredentials:Username"] = dbCredentials.Username;
        builder.Configuration["DatabaseCredentials:Password"] = dbCredentials.Password;

        // Fetch other secrets from Vault (static credentials)
        try
        {
            var secrets = await vaultService.FetchSecretsAsync(
                $"secret/{builder.Environment.EnvironmentName}",
                "jwt_key", "minio_access_key", "minio_secret_key", "rabbitmq_password"
            );

            // Inject static secrets into configuration
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = secrets["jwt_key"],
            ["MassTransit:RabbitMq:Password"] = secrets["rabbitmq_password"],
            ["MinIO:AccessKey"] = secrets["minio_access_key"],
            ["MinIO:SecretKey"] = secrets["minio_secret_key"]
        });


            // Log the configuration values
            var loggerFactory = builder.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<Program>();

            logger.LogInformation("Secrets from Vault:");
            logger.LogInformation("Jwt Key: {JwtKey}", builder.Configuration["Jwt:Key"]);
            logger.LogInformation("Postgres Vault Username: {VaultUsername}", dbCredentials.Username);
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

        // Add DbContext using IOptionsMonitor<DatabaseCredentials>
        builder.Services.AddDbContext<RitualWorksContext>((serviceProvider, options) =>
        {
            var dbCredsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<DatabaseCredentials>>();

            // Function to configure the DbContext
            void ConfigureDbContext(DatabaseCredentials creds)
            {
                var connectionString = $"Host=postgres_primary;Port=5432;Database=your_postgres_db;Username={creds.Username};Password={creds.Password}";
                options.UseNpgsql(connectionString);
            }

            // Set initial configuration with the current credentials
            ConfigureDbContext(dbCredsMonitor.CurrentValue);

            // Update DbContext whenever the credentials change
            dbCredsMonitor.OnChange(creds =>
            {
                ConfigureDbContext(creds);
            });
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

        // Register MinioClient as a singleton
        var minioConfig = builder.Configuration.GetSection("MinIO").Get<MinioSettings>();
        builder.Services.AddSingleton<MinioClient>(sp =>
        {
            return (MinioClient)new MinioClient()
                .WithEndpoint(minioConfig.Endpoint)
                .WithCredentials(minioConfig.AccessKey, minioConfig.SecretKey)
                .WithSSL(minioConfig.Secure) // Use SSL if 'Secure' is true
                .Build();
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

             options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var token = context.Request.Cookies["jwt"];
                    if (!string.IsNullOrEmpty(token))
                    {
                        context.Token = token;
                    }
                    return Task.CompletedTask;
                }
            };
            
        });

        // Configure Stripe
        builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
        StripeConfiguration.ApiKey = builder.Configuration.GetSection("Stripe")["SecretKey"];

        // Add repository and service dependencies
        builder.Services.AddScoped<IFileStorageService, LocalFileSystemService>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<OrderService>();
        builder.Services.AddScoped<ISignedUrlService, SignedUrlService>();
        builder.Services.AddScoped<IAssetService, AssetService>();  

        // Register BlobServiceClient
        builder.Services.AddSingleton(x =>
        {
            var blobSettings = x.GetRequiredService<IOptions<BlobSettings>>().Value;
            return new BlobServiceClient(blobSettings.ConnectionString);
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

        // Add the CredentialRefreshService
        builder.Services.AddHostedService<CredentialRefreshService>();
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
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "RitualWorks API V1");
            c.RoutePrefix = "swagger";
        });

        app.MapControllers();

        await Task.CompletedTask;
    }
}
