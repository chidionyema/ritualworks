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
using Stripe;
using RitualWorks.Repositories.RitualWorks.Repositories;
using MassTransit;
using Amazon.S3;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = CreateWebHostBuilder(args);
        var app = builder.Build();

        await ConfigurePipeline(app);

        app.Run();
    }

    public static WebApplicationBuilder CreateWebHostBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add environment variables before appsettings.json
        builder.Configuration
            .AddEnvironmentVariables()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        // Configure BlobSettings
        builder.Services.Configure<BlobSettings>(builder.Configuration.GetSection("AzureBlobStorage"));

        // Register VaultService with HttpClient support
        builder.Services.Configure<VaultSettings>(builder.Configuration.GetSection("Vault"));
        builder.Services.AddHttpClient<VaultService>();

        // Add MassTransit configuration with RabbitMQ settings
        builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.AddConsumer<MyConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                var rabbitMqHost = builder.Configuration["MassTransit:RabbitMq:Host"];
                cfg.Host(new Uri(rabbitMqHost), h =>
                {
                    h.Username(builder.Configuration["MassTransit:RabbitMq:Username"]);
                    h.Password(builder.Configuration["MassTransit:RabbitMq:Password"]);
                    h.Heartbeat(10); // Heartbeat interval
                    h.RequestedConnectionTimeout(TimeSpan.FromSeconds(30)); // Connection timeout
                });

                // Additional MassTransit configurations
                cfg.PrefetchCount = 16;
                cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
                cfg.UseInMemoryOutbox();

                cfg.ConfigureEndpoints(context);
            });
        });

        // Add services to the container
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
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
            };
        });

        // Configure Stripe
        builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
        StripeConfiguration.ApiKey = builder.Configuration.GetSection("Stripe")["SecretKey"];

        // Add DbContext
        builder.Services.AddDbContext<RitualWorksContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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

        return builder;
    }

    public static async Task ConfigurePipeline(WebApplication app)
    {
        var vaultService = app.Services.GetRequiredService<VaultService>();

        // Fetch secrets from Vault for various services (e.g., JWT, PostgreSQL, RabbitMQ, MinIO)
        var secrets = await vaultService.FetchSecretsAsync($"secret/data/{app.Environment.EnvironmentName}",
            "jwt_key", "postgres_password", "minio_access_key", "minio_secret_key", "rabbitmq_password");

        var configuration = app.Configuration;

        // Inject secrets into configuration
        configuration["Jwt:Key"] = secrets["jwt_key"];
        configuration["ConnectionStrings:DefaultConnection"] =
            $"Host=postgres_primary;Port=5432;Database=your_postgres_db;Username=myuser;Password={secrets["postgres_password"]}";

        configuration["MassTransit:RabbitMq:Password"] = secrets["rabbitmq_password"];
        configuration["MinIO:AccessKey"] = secrets["minio_access_key"];
        configuration["MinIO:SecretKey"] = secrets["minio_secret_key"];

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

        app.Use(async (context, next) =>
        {
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; frame-src 'self'";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
            await next();
        });

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "RitualWorks API V1");
            c.RoutePrefix = "swagger";
        });

        app.MapControllers();
    }
}
