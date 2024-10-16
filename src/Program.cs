using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
using System.IO;
using System.Text.Json;
using Stripe;
using MassTransit;
using Minio;
using RitualWorks.Models;
using Serilog;
using Serilog.Events;
using Microsoft.AspNetCore.Identity;
using System.Security.Authentication;


public partial class Program
{
    private static string _dbCredsFilePath = "/vault/secrets/db-creds.json";

    public static async Task Main(string[] args)
    {
        ConfigureLogging();

        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog();

        ConfigureEnvironmentVariables(builder);

        // Load database credentials from the file
        var dbCredentials = LoadDbCredentialsFromFile();
        if (dbCredentials == null)
        {
            throw new InvalidOperationException("Failed to retrieve database credentials from the file.");
        }

        // Inject the database credentials into the configuration
        InjectDbCredentialsIntoConfiguration(builder, dbCredentials);

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

    // Load the database credentials from the JSON file
   private static DatabaseCredentials LoadDbCredentialsFromFile()
{
    var credentialsFilePath = _dbCredsFilePath;

    if (!System.IO.File.Exists(credentialsFilePath))
    {
        throw new FileNotFoundException($"Vault credentials file not found at: {credentialsFilePath}");
    }

    var jsonContent = System.IO.File.ReadAllText(credentialsFilePath);
    Console.WriteLine($"Loaded  json Content: {jsonContent}");

    var dbCredentials = JsonSerializer.Deserialize<DatabaseCredentials>(jsonContent);
    
    // Log to verify
    Console.WriteLine($"Loaded DB credentials: {dbCredentials.Username}, {dbCredentials.Password}");

    return dbCredentials;
}


    // Inject the database credentials into the configuration
    private static void InjectDbCredentialsIntoConfiguration(WebApplicationBuilder builder, DatabaseCredentials dbCredentials)
    {
        var existingConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");

        // Parse the connection string and replace the Username and Password
        var updatedConnectionString = new Npgsql.NpgsqlConnectionStringBuilder(existingConnectionString)
        {
            Username = dbCredentials.Username,
            Password = dbCredentials.Password
        }.ConnectionString;

        // Update the connection string in the configuration
        builder.Configuration["ConnectionStrings:DefaultConnection"] = updatedConnectionString;
    }

    public static async Task ConfigureServicesAsync(WebApplicationBuilder builder)
    {
        builder.Services.AddAutoMapper(typeof(MappingProfile));
        // Add Identity services
        builder.Services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<RitualWorksContext>()
            .AddDefaultTokenProviders();

        ConfigureDbContext(builder);
        ConfigureServices(builder);
        AddMassTransit(builder);
        AddMinioClient(builder);
        AddSwagger(builder);
        AddCredentialRefreshService(builder);
    }

    private static void ConfigureDbContext(WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<RitualWorksContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            // Use the connection string to configure Npgsql
            options.UseNpgsql(connectionString);
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
                var rabbitMqCertPath = builder.Configuration["MassTransit:RabbitMq:Ssl:CertificatePath"];
                var rabbitMqCertPassphrase = builder.Configuration["MassTransit:RabbitMq:Ssl:CertificatePassphrase"];
                var useCertAsAuth = bool.Parse(builder.Configuration["MassTransit:RabbitMq:Ssl:UseCertificateAsAuthentication"]);

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
                            ssl.CertificatePath = rabbitMqCertPath;
                            ssl.CertificatePassphrase = rabbitMqCertPassphrase;
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
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "RitualWorks API V1");
            c.RoutePrefix = "swagger";
        });

        app.MapControllers();
        await Task.CompletedTask;
    }
}
