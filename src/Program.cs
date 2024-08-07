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
using RitualWorks;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using IdentityServer4.Models;
using MassTransit;

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

        // Load configuration from appsettings.json
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        // Configure BlobSettings
        builder.Services.Configure<BlobSettings>(builder.Configuration.GetSection("AzureBlobStorage"));

        builder.Services.AddMassTransit(x =>
        {
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host("rabbitmq://ritualworks-rabbitmq-1");

                // Configure prefetch count to optimize message consumption
                cfg.PrefetchCount = 16;

                // Configure the message retry policy
                cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

                // Configure in-memory outbox for temporary message storage
                cfg.UseInMemoryOutbox();

                cfg.ReceiveEndpoint("queue1", ep =>
                {
                    ep.ConfigureConsumer<MyConsumer>(context);
                    ep.Bind("exchange1", x =>
                    {
                        x.RoutingKey = "key1";
                    });
                });
            });

            x.AddConsumer<MyConsumer>();
        });

        builder.Services.AddMassTransitHostedService();
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
        builder.Services.AddScoped<IPetitionRepository, PetitionRepository>();
        builder.Services.AddScoped<IDonationRepository, DonationRepository>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IProductRepository, ProductRepository>();
        builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<IPaymentService, StripePaymentService>();
        builder.Services.AddScoped<CheckoutService>();


        builder.Services.AddScoped<IRitualService, RitualService>();
        builder.Services.AddScoped<IPetitionService, PetitionService>();
        builder.Services.AddScoped<IDonationService, DonationService>();
        builder.Services.AddScoped<IPostRepository, PostRepository>();
        builder.Services.AddScoped<ICommentRepository, CommentRepository>();
        builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();


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

        app.UseAuthentication();
        app.UseAuthorization();

        app.Use(async (context, next) =>
        {
            context.Response.Headers.Add("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self'; frame-src 'self'");
            context.Response.Headers.Add("X-Frame-Options", "DENY");
            context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
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
