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

        // Configure HTTPS redirection. REMOVED FOR TESTING
        // services.AddHttpsRedirection(options =>
        // {
        //     options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
        //     options.HttpsPort = 443;
        // });

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

        // Create token validation parameters once
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            NameClaimType = ClaimTypes.NameIdentifier,
            ClockSkew = TimeSpan.Zero
        };

        // Share the same token parameters
        services.AddSingleton(tokenValidationParameters);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme; // Set default to Bearer
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme; // Set default to Bearer
        })
        .AddPolicyScheme("MultiAuth", "MultiAuth", policyOpts =>
        {
            policyOpts.ForwardDefaultSelector = context =>
            {
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

                // Check for JWT cookie first - this is important for proper precedence
                if (context.Request.Cookies.ContainsKey("jwt"))
                {
                    logger.LogInformation("JWT cookie found, using CookieAuth scheme");
                    return "CookieAuth";
                }

                // Then check for Authorization header
                if (context.Request.Headers.ContainsKey("Authorization"))
                {
                    var authHeader = context.Request.Headers["Authorization"].ToString();
                    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogInformation("Authorization header found, using Bearer scheme");
                        return JwtBearerDefaults.AuthenticationScheme;
                    }
                }

                logger.LogInformation("No auth detected, defaulting to Bearer scheme");
                return JwtBearerDefaults.AuthenticationScheme;
            };
        })
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, bearerOpts =>
        {
            bearerOpts.SaveToken = true;
            bearerOpts.RequireHttpsMetadata = false;
            bearerOpts.TokenValidationParameters = tokenValidationParameters;

            // Add events for better debugging and handling
            bearerOpts.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogError(context.Exception, "JWT authentication failed");
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogInformation("JWT token validated successfully for {Identity}",
                        context.Principal?.Identity?.Name ?? "unknown");

                    // Add any additional claims or processing here
                    return Task.CompletedTask;
                },
                OnMessageReceived = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

                    // Check if token is already set
                    if (context.Token != null)
                    {
                        logger.LogInformation("JWT token already set: {TokenLength} chars", context.Token.Length);
                        return Task.CompletedTask;
                    }

                    // Extract from Authorization header
                    if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
                    {
                        var authValue = authHeader.ToString();
                        if (authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Token = authValue.Substring("Bearer ".Length).Trim();
                            logger.LogInformation("Extracted token from Authorization header: {TokenLength} chars",
                                context.Token.Length);
                        }
                    }

                    // Also check the cookie as a fallback
                    if (context.Token == null && context.Request.Cookies.ContainsKey("jwt"))
                    {
                        context.Token = context.Request.Cookies["jwt"];
                        logger.LogInformation("Extracted token from cookie: {TokenLength} chars", context.Token.Length);
                    }

                    return Task.CompletedTask;
                }
            };
        })
        .AddScheme<CookieAuthenticationOptions, JwtInCookieHandler>("CookieAuth", options =>
        {
            options.Cookie.Name = "jwt"; // Match the cookie name
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;

            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }
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

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            try
            {
                // Check if the cookie exists
                if (!Request.Cookies.TryGetValue("jwt", out var jwtValue) || string.IsNullOrEmpty(jwtValue))
                {
                    _logger.LogDebug("No jwt cookie found in the request");
                    return AuthenticateResult.NoResult();
                }

                _logger.LogDebug("Found jwt cookie with length: {Length}", jwtValue.Length);
                var maskedJwt = jwtValue.Length > 8
            ? $"{jwtValue.Substring(0, 4)}...{jwtValue.Substring(jwtValue.Length - 4)}"
            : jwtValue;
                _logger.LogDebug(" security extensions Found jwt cookie with length: {Length} and token: {Token}", jwtValue.Length, maskedJwt);


                // Validate the token
                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(jwtValue))
                {
                    _logger.LogWarning("JWT cookie contains invalid token format");
                    return AuthenticateResult.Fail("Invalid token format");
                }

                // Use same token validation parameters as for the Bearer scheme
                ClaimsPrincipal principal;
                SecurityToken validatedToken;

                try
                {
                    principal = handler.ValidateToken(jwtValue, _tokenValidationParams, out validatedToken);
                }
                catch (SecurityTokenExpiredException ex)
                {
                    _logger.LogInformation("JWT cookie expired: {Message}", ex.Message);
                    return AuthenticateResult.Fail("Token expired");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("JWT validation error: {Message}", ex.Message);
                    return AuthenticateResult.Fail("Invalid token");
                }

                // Check token expiration (belt and suspenders approach)
                var jwtToken = validatedToken as JwtSecurityToken;
                if (jwtToken == null || jwtToken.ValidTo < DateTime.UtcNow)
                {
                    _logger.LogWarning("Token is expired: {Expiry}", jwtToken?.ValidTo);
                    return AuthenticateResult.Fail("Token expired");
                }

                _logger.LogInformation("JWT cookie validated successfully for user: {User}",
                    principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown");

                // Create authentication ticket
                var ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), Scheme.Name);
                return AuthenticateResult.Success(ticket);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JWT cookie validation failed: {Message}", ex.Message);
                return AuthenticateResult.Fail(ex);
            }
        }

    }
}