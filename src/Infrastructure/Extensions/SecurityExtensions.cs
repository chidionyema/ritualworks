using System;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NWebsec.AspNetCore.Middleware;
using System.Threading.RateLimiting;
using haworks.Models;
using haworks.Db;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;


 /// <summary>
    /// All security-related extension methods (CORS, HSTS, authentication, etc.)
    /// </summary>
    public static class SecurityExtensions
    {
        /// <summary>
        /// Registers HTTP security services for header hardening (antiforgery, CORS, HSTS, HTTPS redirection).
        /// </summary>
        public static IServiceCollection AddContentSecurity(this IServiceCollection services, IConfiguration config)
        {
            // Configure antiforgery (CSRF protection).
            services.AddAntiforgery(options =>
            {
                options.HeaderName = "X-CSRF-TOKEN";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });

            // Configure CORS for upload operations.
            services.AddCors(options =>
            {
                options.AddPolicy("UploadPolicy", policy =>
                {
                    var origins = config["AllowedOrigins"]?.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                  ?? Array.Empty<string>();
                    policy.WithOrigins(origins)
                          .WithMethods("POST", "PUT", "DELETE")
                          .WithHeaders("Content-Type", "Authorization")
                          .WithExposedHeaders("Content-Disposition");
                });
            });

            // Configure HSTS.
            services.AddHsts(options =>
            {
                options.Preload = true;
                options.IncludeSubDomains = true;
                options.MaxAge = TimeSpan.FromDays(365);
            });

            // Configure HTTPS redirection.
            services.AddHttpsRedirection(options =>
            {
                options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
                options.HttpsPort = 443;
            });

            return services;
        }

        /// <summary>
        /// Registers authentication, authorization, identity, rate limiting, and additional CORS policies.
        /// </summary>
        public static IServiceCollection AddSecurityServices(this IServiceCollection services, IConfiguration config)
        {
            // Add JWT authentication, custom Cookie-based JWT, authorization, Identity, and rate limiting.
            services.AddAuthenticationSchemes(config)
                    .AddAuthorizationPolicies()
                    .AddRateLimiting(config);

            // Additionally, add a specific CORS policy for a front-end (e.g., localhost:3001).
            services.AddCors(options =>
            {
                options.AddPolicy("Allow3001",
                    policy => policy.WithOrigins("http://localhost:3001")
                                    .AllowAnyHeader()
                                    .AllowAnyMethod());
            });

            return services;
        }

        /// <summary>
        /// Adds security headers to the HTTP response (including Content Security Policy via NWebsec).
        /// </summary>
        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IConfiguration config)
        {
            app.UseCsp(options => options
                .DefaultSources(s => s.Self())
                .ScriptSources(s => s.Self().CustomSources("https://trusted.cdn.com"))
                .StyleSources(s => s.Self().CustomSources("https://trusted.styles.com"))
                // Add additional directives as needed.
            );

            return app;
        }

         /// <summary>
        /// Seeds the required roles (e.g., ContentUploader) during application startup.
        /// </summary>
        public static async Task SeedRolesAsync(this IApplicationBuilder app)
        {
            using (var scope = app.ApplicationServices.CreateScope())
            {
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

                // Check if the "ContentUploader" role exists and create it if it doesn't.
                if (!await roleManager.RoleExistsAsync("ContentUploader"))
                {
                    var result = await roleManager.CreateAsync(new IdentityRole("ContentUploader"));
                    if (!result.Succeeded)
                    {
                        // Optionally, handle errors (e.g., logging)
                        throw new Exception("Failed to create ContentUploader role");
                    }
                }
            }
        }

        // ---- PRIVATE EXTENSIONS ----

        /// <summary>
        /// Top-level method that sets up a "policy scheme" called MultiAuth, 
        /// which tries Cookie first or Bearer second, plus the actual Bearer/Cookie registrations.
        /// </summary>
        private static IServiceCollection AddAuthenticationSchemes(this IServiceCollection services, IConfiguration config)
        {
            var jwtSection = config.GetSection("Jwt");
            var keyBytes = Convert.FromBase64String(jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key missing!"));

            services.Configure<TokenValidationParameters>(opts =>
            {
                opts.ValidateIssuer = true;
                opts.ValidIssuer = jwtSection["Issuer"];
                opts.ValidateAudience = true;
                opts.ValidAudience = jwtSection["Audience"];
                opts.ValidateLifetime = true;
                opts.ValidateIssuerSigningKey = true;
                opts.IssuerSigningKey = new SymmetricSecurityKey(keyBytes);
                opts.NameClaimType = ClaimTypes.NameIdentifier;
                opts.ClockSkew = TimeSpan.Zero;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultScheme = "MultiAuth";
                options.DefaultChallengeScheme = "MultiAuth";
            })
            .AddPolicyScheme("MultiAuth", "CookieOrBearer", policyOpts =>
            {
                policyOpts.ForwardDefaultSelector = context =>
                {
                    string? authHeader = context.Request.Headers["Authorization"];
                    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        return JwtBearerDefaults.AuthenticationScheme;
                    if (context.Request.Cookies.ContainsKey("jwt"))
                        return "CookieAuth";
                    return JwtBearerDefaults.AuthenticationScheme;
                };
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, bearerOpts =>
            {
                // Retrieve the centralized TokenValidationParameters from DI
                var tvp = services.BuildServiceProvider().GetRequiredService<IOptions<TokenValidationParameters>>().Value;
                bearerOpts.TokenValidationParameters = tvp;
            })
            .AddScheme<CookieAuthenticationOptions, JwtInCookieHandler>("CookieAuth", options =>
            {
                options.LoginPath = string.Empty;
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; },
                    OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; }
                };
            });

            return services;
        }

      private static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        // Register authorization policies
        services.AddAuthorization(options =>
        {
            // **1. Existing "AdminOnly" Policy (Leave this as it is):**
            options.AddPolicy("AdminOnly", policy =>
                policy.RequireClaim(ClaimTypes.Role, "Admin"));

           options.AddPolicy("ContentUploader", policy =>
            {
                policy.RequireAuthenticatedUser()
                    .RequireRole("ContentUploader") // Match the role claim
                    .RequireClaim("permission", "upload_content"); // Match the custom claim
            });
        });

        return services;
    }

        private static IServiceCollection AddRateLimiting(this IServiceCollection services, IConfiguration config)
        {
            services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetFixedWindowLimiter("Global", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromSeconds(60),
                        QueueLimit = 5
                    }));
                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = 429;
                    await context.HttpContext.Response.WriteAsync("Too many requests.", cancellationToken);
                };
            });
            return services;
        }
    }

    /// <summary>
    /// Custom cookie auth handler that reads a JWT from a cookie named 'jwt',
    /// validates it with the same TokenValidationParameters as Bearer JWT.
    /// </summary>
    internal class JwtInCookieHandler : CookieAuthenticationHandler
    {
        private readonly TokenValidationParameters _tokenValidationParams;
        private readonly ILogger<JwtInCookieHandler> _logger;

        public JwtInCookieHandler(
            IOptionsMonitor<CookieAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            IOptions<TokenValidationParameters> tokenValidationParams) 
            : base(options, logger, encoder, clock)
        {
            _tokenValidationParams = tokenValidationParams.Value;
            _logger = logger.CreateLogger<JwtInCookieHandler>();
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Cookies.TryGetValue("jwt", out var jwtValue) || string.IsNullOrEmpty(jwtValue))
            {
                Logger.LogDebug("No jwt cookie found.");
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var principal = handler.ValidateToken(jwtValue, _tokenValidationParams, out var validatedToken);
                Logger.LogDebug("JWT cookie validated successfully. Subject: {Subject}", principal.Identity?.Name);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
            catch (Exception ex)
            {
                 Logger.LogError(ex, "JWT cookie validation failed.");
                return Task.FromResult(AuthenticateResult.Fail("Invalid JWT cookie"));
            }
        }

    }
