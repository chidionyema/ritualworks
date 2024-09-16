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
using System.Security.Authentication;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = CreateWebHostBuilder(args);
        var app = builder.Build();

        ConfigurePipeline(app);

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

        // Add MassTransit configuration with RabbitMQ settings
        
            builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            x.AddConsumer<MyConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                // Read RabbitMQ settings from the configuration
                var rabbitMqHost = builder.Configuration["MassTransit:RabbitMq:Host"];
                var rabbitMqUsername = builder.Configuration["MassTransit:RabbitMq:Username"];
                var rabbitMqPassword = builder.Configuration["MassTransit:RabbitMq:Password"];
                var heartbeatInterval = builder.Configuration.GetValue<int>("MassTransit:RabbitMq:HeartbeatInterval", 10);
                var connectionTimeoutSeconds = builder.Configuration.GetValue<int>("MassTransit:RabbitMq:ConnectionTimeoutSeconds", 30);

                cfg.Host(new Uri(rabbitMqHost), h =>
                {
                    h.Username(rabbitMqUsername);
                    h.Password(rabbitMqPassword);
                    h.Heartbeat((ushort)heartbeatInterval);// Use configured heartbeat interval
                    h.RequestedConnectionTimeout(TimeSpan.FromSeconds(connectionTimeoutSeconds)); // Use configured connection timeout
                });

                // Additional MassTransit configurations
                cfg.PrefetchCount = 16;
                cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
                cfg.UseInMemoryOutbox();

                cfg.ConfigureEndpoints(context);
            });
        });

        // Add services to the container.
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

        builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
        // Configure Stripe
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

    public static void ConfigurePipeline(WebApplication app)
    {
        // Configure the HTTP request pipeline.
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

        // Enable CORS
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

        // Enable middleware to serve generated Swagger as a JSON endpoint.
        app.UseSwagger();

        // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
        // specifying the Swagger JSON endpoint.
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "RitualWorks API V1");
            c.RoutePrefix = "swagger"; // Swagger UI will be served at /swagger
        });

        app.MapControllers();
    }
}
