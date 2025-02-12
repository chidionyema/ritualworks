﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using haworks.Models;
using haworks.Dto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System;
using System.Collections.Generic;
using haworks.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using System.Linq;
using System.ComponentModel.DataAnnotations;

namespace haworks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthenticationController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthenticationController> _logger;
        private readonly haworksContext _context;
        private readonly RsaSecurityKey _rsaSecurityKey;
        private readonly TokenValidationParameters _tokenValidationParameters;

        public AuthenticationController(
            UserManager<User> userManager,
            IConfiguration configuration,
            ILogger<AuthenticationController> logger,
            haworksContext context)
        {
            _userManager = userManager;
            _configuration = configuration;
            _logger = logger;
            _context = context;
            var rsa = RSA.Create();
            // **Production Secret Management:** Consider Azure Key Vault, AWS Secrets Manager, etc. instead of appsettings/env vars for private key in real prod.
            rsa.ImportFromPem(configuration["Jwt:PrivateKey"]);
            _rsaSecurityKey = new RsaSecurityKey(rsa);
            _tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero,
                IssuerSigningKey = _rsaSecurityKey
            };
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserRegistrationDto registrationDto)
        {
            if (!ModelState.IsValid) // Input Validation
            {
                _logger.LogWarning("Invalid registration attempt. Model state errors: {ModelStateErrors}", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return BadRequest(ModelState); // Return validation errors
            }

            var user = new User
            {
                UserName = registrationDto.Username,
                Email = registrationDto.Email
            };
            var result = await _userManager.CreateAsync(user, registrationDto.Password);
            if (!result.Succeeded)
            {
                _logger.LogWarning("User registration failed for username: {Username}. Errors: {Errors}", registrationDto.Username, string.Join(", ", result.Errors.Select(e => e.Description)));
                return BadRequest(result.Errors); // Return Identity errors
            }

            _logger.LogInformation("User registered successfully: {Username}, UserId: {UserId}", user.UserName, user.Id); // Security Logging (success)

            var token = GenerateToken(user.UserName, user.Id, DateTime.UtcNow.AddMinutes(15));
            SetSecureCookie(token);
            return Ok(new { Token = new JwtSecurityTokenHandler().WriteToken(token), UserId = user.Id, Expires = token.ValidTo });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginDto loginDto)
        {
            if (!ModelState.IsValid) // Input Validation
            {
                _logger.LogWarning("Invalid login attempt. Model state errors: {ModelStateErrors}", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return BadRequest(ModelState); // Return validation errors
            }

            var user = await _userManager.FindByNameAsync(loginDto.Username);
            if (user == null || !await _userManager.CheckPasswordAsync(user, loginDto.Password))
            {
                _logger.LogWarning("Failed login attempt for username: {Username}", loginDto.Username); // Security Logging (failed login)
                return Unauthorized(new { message = "Invalid credentials" }); // JSON Error response for invalid credentials
            }

            _logger.LogInformation("User logged in successfully: {Username}, UserId: {UserId}", user.UserName, user.Id); // Security Logging (success)

            var token = GenerateToken(user.UserName, user.Id, DateTime.UtcNow.AddMinutes(15));
            SetSecureCookie(token);

            // **Determine Subscription Status:**
            bool isUserSubscribed = await _context.Subscriptions.AnyAsync(s => s.UserId == user.Id && s.Status == SubscriptionStatus.Active);

            // **Include isSubscribed in the response:**
            return Ok(new { Token = new JwtSecurityTokenHandler().WriteToken(token), User = new { Id = user.Id, UserName = user.UserName, Email = user.Email, isSubscribed = isUserSubscribed }, Expires = token.ValidTo });
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("jwt");
            _logger.LogInformation("User logged out. IP Address: {IPAddress}", HttpContext.Connection.RemoteIpAddress?.ToString()); // Security Logging (logout)
            return Ok(new { Message = "Logged out successfully" });
        }

        [HttpGet("verify-token")]
        public async Task<IActionResult> VerifyToken()
        {
            var token = GetTokenFromHeader();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("VerifyToken: No token found in header. IP Address: {IPAddress}", HttpContext.Connection.RemoteIpAddress?.ToString()); // Security Logging (no token)
                return Unauthorized(new { message = "Invalid token" }); // JSON Error response for invalid token
            }

            var principal = ValidateToken(token);
            if (principal == null)
            {
                _logger.LogWarning("VerifyToken: Token validation failed. Token: {Token}, IP Address: {IPAddress}", token, HttpContext.Connection.RemoteIpAddress?.ToString()); // Security Logging (invalid token)
                return Unauthorized(new { message = "Token validation failed" }); // JSON Error response for token validation failure
            }

            _logger.LogDebug("Token verified successfully for user: {Username}, UserId: {UserId}", principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, principal.FindFirst(ClaimTypes.NameIdentifier)?.Value); // Debug logging for token verification

            string userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

            // **Determine Subscription Status:**
            bool isUserSubscribed = await _context.Subscriptions.AnyAsync(s => s.UserId == userId && s.Status == SubscriptionStatus.Active);

            // **Include isSubscribed in the response:**
            return Ok(new { UserName = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, UserId = userId, isSubscribed = isUserSubscribed });
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            var principal = ValidateToken(request.AccessToken);
            if (principal == null)
            {
                _logger.LogWarning("RefreshToken: Access token validation failed. IP Address: {IPAddress}", HttpContext.Connection.RemoteIpAddress?.ToString());
                return Unauthorized(new { message = "Invalid token" }); // JSON Error Response
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var storedToken = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken && rt.UserId == userId);
            if (storedToken == null || storedToken.Expires < DateTime.UtcNow)
            {
                _logger.LogWarning("RefreshToken: Invalid or expired refresh token used for UserID: {UserId}. Provided Refresh Token: {RefreshToken}", userId, request.RefreshToken);
                return Unauthorized(new { message = "Invalid refresh token" }); // JSON Error Response
            }

            _context.RefreshTokens.Remove(storedToken);
            await _context.SaveChangesAsync();

            var newAccessToken = GenerateToken(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), userId, DateTime.UtcNow.AddMinutes(15));
            var newRefreshToken = await GenerateRefreshToken(userId);

            _logger.LogInformation("Token refreshed successfully for UserId: {UserId}", userId); // Security logging (token refresh)

            return Ok(new { AccessToken = new JwtSecurityTokenHandler().WriteToken(newAccessToken), RefreshToken = newRefreshToken.Token, Expires = newAccessToken.ValidTo });
        }

        [HttpGet("external/microsoft")]
        public IActionResult LoginMicrosoft()
        {
            var redirectUrl = Url.Action("MicrosoftCallback", "Authentication", null, Request.Scheme); // Ensure scheme is included for correct redirect URL
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl, Items = { { "scheme", MicrosoftAccountDefaults.AuthenticationScheme } } };
            return Challenge(properties, MicrosoftAccountDefaults.AuthenticationScheme);
        }

        [HttpGet("external/microsoft-callback")]
        public async Task<IActionResult> MicrosoftCallback()
        {
            var result = await HttpContext.AuthenticateAsync(MicrosoftAccountDefaults.AuthenticationScheme);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Microsoft external authentication failed. Error: {Error}", result.Failure?.Message);
                return BadRequest(new { message = "External authentication failed" }); // JSON Error Response
            }

            var email = result.Principal.FindFirstValue(ClaimTypes.Email);
            var user = await _userManager.FindByEmailAsync(email) ?? await CreateUserFromExternalProvider(email);

            // **Determine Subscription Status for External Login:**
            bool isUserSubscribed = await _context.Subscriptions.AnyAsync(s => s.UserId == user.Id && s.Status == SubscriptionStatus.Active);


            var accessToken = GenerateToken(user.UserName, user.Id, DateTime.UtcNow.AddMinutes(15));
            var refreshToken = await GenerateRefreshToken(user.Id);
            SetSecureCookie(accessToken);

            _logger.LogInformation("Microsoft external login successful for user: {Email}, UserId: {UserId}", email, user.Id); // Security Logging (external login success)

            return Ok(new { AccessToken = new JwtSecurityTokenHandler().WriteToken(accessToken), RefreshToken = refreshToken.Token, Expires = accessToken.ValidTo, User = new { Id = user.Id, UserName = user.UserName, Email = user.Email, isSubscribed = isUserSubscribed } });
        }

        private JwtSecurityToken GenerateToken(string userName, string userId, DateTime expiration)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, userName),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
                new(ClaimTypes.NameIdentifier, userId),
                new("auth_time", DateTime.UtcNow.ToString("O"))
            };
            return new JwtSecurityToken(_configuration["Jwt:Issuer"], _configuration["Jwt:Audience"], claims, expires: expiration, signingCredentials: new SigningCredentials(_rsaSecurityKey, SecurityAlgorithms.RsaSsaPssSha256));
        }

        private async Task<User> CreateUserFromExternalProvider(string email)
        {
            var user = new User { UserName = email, Email = email, EmailConfirmed = true };
            var result = await _userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                _logger.LogError("Failed to create user from external provider for email: {Email}. Errors: {Errors}", email, string.Join(", ", result.Errors.Select(e => e.Description)));
                throw new Exception("User creation failed from external provider");
            }
            _logger.LogInformation("User created from external provider: {Email}, UserId: {UserId}", email, user.Id); 
            return user;
        }

        private async Task<RefreshToken> GenerateRefreshToken(string userId)
        {
            var refreshToken = new RefreshToken
            {
                UserId = userId,
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                Expires = DateTime.UtcNow.AddDays(7),
                Created = DateTime.UtcNow
            };
            _context.RefreshTokens.Add(refreshToken);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error while saving refresh token for UserId: {UserId}", userId);
                throw; 
            }
            return refreshToken;
        }

        private ClaimsPrincipal? ValidateToken(string token)
        {
            try
            {
                return new JwtSecurityTokenHandler().ValidateToken(token, _tokenValidationParameters, out _);
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogWarning("Token validation failed: {Message}. Token: {Token}", ex.Message, token); 
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during token validation. Token: {Token}", token); 
                return null;
            }
        }

        private string? GetTokenFromHeader()
        {
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return null; // No Bearer token found
            }
            return authHeader["Bearer ".Length..].Trim();
        }


        private void SetSecureCookie(JwtSecurityToken token)
        {
            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            Response.Cookies.Append("jwt", tokenString, new CookieOptions
            {
                HttpOnly = true,
                Secure = !_configuration["ASPNETCORE_ENVIRONMENT"]?.Equals("Development", StringComparison.OrdinalIgnoreCase) ?? true,
                SameSite = SameSiteMode.Strict,
                Expires = token.ValidTo
            });
        }
    }

    // DTOs (Keep DTO classes in AuthenticationController.cs for simplicity in this example)

    public class UserRegistrationDto
    {
        [Required(ErrorMessage = "Username is required")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]+$",
            ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one digit and one special character")]
        public string Password { get; set; } = string.Empty;
    }

    public class UserLoginDto
    {
        [Required(ErrorMessage = "Username is required")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; } = string.Empty;
    }

    public class RefreshTokenRequest
    {
        [Required(ErrorMessage = "Access Token is required")]
        public string AccessToken { get; set; } = string.Empty;

        [Required(ErrorMessage = "Refresh Token is required")]
        public string RefreshToken { get; set; } = string.Empty;
    }

}