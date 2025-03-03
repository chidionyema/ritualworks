using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using haworks.Models;
using haworks.Dto;
using haworks.Db;
using haworks.Services;
using Haworks.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;

namespace haworks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthenticationController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly ILogger<AuthenticationController> _logger;
        private readonly IdentityContext _identityContext;
        private readonly AuthService _authService;

        public AuthenticationController(
            UserManager<User> userManager,
            ILogger<AuthenticationController> logger,
            IdentityContext identityContext,
            AuthService authService)
        {
            _userManager = userManager;
            _logger = logger;
            _identityContext = identityContext;
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserRegistrationDto registrationDto)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Invalid registration attempt => Model errors: {Errors}",
                    string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Attempting to register user: {Username}", registrationDto.Username);
            var user = new User
            {
                UserName = registrationDto.Username,
                Email = registrationDto.Email
            };

            var result = await _userManager.CreateAsync(user, registrationDto.Password);
            if (!result.Succeeded)
            {
                foreach (var err in result.Errors)
                {
                    _logger.LogWarning("Registration error: {Err}", err.Description);
                }
                return BadRequest(result.Errors);
            }

            _logger.LogInformation("User registration succeeded for user: {Username}, Id: {UserId}",
                user.UserName, user.Id);

            var roleResult = await _userManager.AddToRoleAsync(user, "ContentUploader");
            if (!roleResult.Succeeded)
                return BadRequest(roleResult.Errors);

            var claimResult = await _userManager.AddClaimAsync(
                user, new Claim("permission", "upload_content")
            );
            if (!claimResult.Succeeded)
                return BadRequest(claimResult.Errors);   

            var token = await _authService.GenerateToken(user, DateTime.UtcNow.AddMinutes(15));
            _authService.SetSecureCookie(HttpContext, token);

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                userId = user.Id,
                expires = token.ValidTo
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginDto loginDto)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Invalid login attempt => Model errors: {Errors}",
                    string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Attempting to login user: {Username}", loginDto.Username);
            var user = await _userManager.FindByNameAsync(loginDto.Username);
            if (user == null)
            {
                _logger.LogWarning("User not found: {Username}", loginDto.Username);
                return Unauthorized(new { message = "Invalid credentials" });
            }

            var passwordValid = await _userManager.CheckPasswordAsync(user, loginDto.Password);
            if (!passwordValid)
            {
                _logger.LogWarning("Invalid password for user: {Username}", loginDto.Username);
                return Unauthorized(new { message = "Invalid credentials" });
            }

            _logger.LogInformation("Login successful for user: {Username}, Id: {UserId}", user.UserName, user.Id);

            var token = await _authService.GenerateToken(user, DateTime.UtcNow.AddMinutes(15));
            var refreshTokenEntity = await _authService.GenerateRefreshToken(user.Id);
            _authService.SetSecureCookie(HttpContext, token);

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                refreshToken = refreshTokenEntity.Token,
                user = new
                {
                    id = user.Id,
                    userName = user.UserName,
                    email = user.Email
                },
                expires = token.ValidTo
            });
        }

        

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            _logger.LogInformation("Logout endpoint called; removing 'jwt' cookie.");
            var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var expiryClaim = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
            
            if (!string.IsNullOrEmpty(jti) && !string.IsNullOrEmpty(userId) && expiryClaim != null)
            {
                var expiryDate = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expiryClaim)).UtcDateTime;
                await _authService.RevokeToken(jti, userId, expiryDate);
            }
            _authService.DeleteAuthCookie(HttpContext);
            return Ok(new { message = "Logged out successfully" });
        }

        [Authorize]
        [HttpGet("verify-token")]
        public async Task<IActionResult> VerifyToken()
        {
            // Since we're using [Authorize], we know the user is authenticated
            // by the time we reach this code
            
            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            }
            
            // Don't check if userId is null here - if we got this far with [Authorize]
            // then we should have a valid authenticated user - log it but don't fail
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Unusual: Authorized user but no userId claim found");
                // Get all claims for debugging
                _logger.LogInformation("Claims present: {claims}", 
                    string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));
                
                // Try to use Identity.Name as fallback
                if (User.Identity?.Name != null)
                {
                    var userByName = await _userManager.FindByNameAsync(User.Identity.Name);
                    if (userByName != null)
                    {
                        userId = userByName.Id;
                    }
                }
                
                // If we still have no userId, return the authenticated identity info anyway
                if (string.IsNullOrEmpty(userId))
                {
                    return Ok(new { 
                        message = "Authenticated but userId not found in claims",
                        identity = User.Identity?.Name,
                        isAuthenticated = User.Identity?.IsAuthenticated ?? false
                    });
                }
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User ID from token ({UserId}) not found in database", userId);
                // Again, we're already authorized, so return some info rather than 401
                return Ok(new { 
                    message = "User ID from token not found in database",
                    userId = userId
                });
            }

            return Ok(new { userId = user.Id, userName = user.UserName });
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            _logger.LogInformation("RefreshToken called with AccessToken length: {AccessLen}, RefreshToken length: {RefreshLen}",
                request.AccessToken.Length, request.RefreshToken.Length);

            try
            {
                var principal = _authService.ValidateToken(request.AccessToken);
                if (principal == null)
                {
                    _logger.LogWarning("RefreshToken => principal is null => returning Unauthorized");
                    return Unauthorized();
                }

                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? 
                             principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
                             
                _logger.LogInformation("Attempting refresh for userId: {UserId}", userId);

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("RefreshToken => user not found => returning Unauthorized");
                    return Unauthorized();
                }

                var storedToken = await _identityContext.RefreshTokens
                    .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken && rt.UserId == userId);

                if (storedToken == null)
                {
                    _logger.LogWarning("RefreshToken => No matching refresh token in DB => Unauthorized");
                    return Unauthorized();
                }

                if (storedToken.Expires < DateTime.UtcNow)
                {
                    _logger.LogWarning("RefreshToken => storedToken expired on {ExpiredOn} => returning Unauthorized",
                        storedToken.Expires);
                    return Unauthorized();
                }

                _identityContext.RefreshTokens.Remove(storedToken);
                await _identityContext.SaveChangesAsync();

                var newAccessToken = await _authService.GenerateToken(user, DateTime.UtcNow.AddMinutes(15));
                var newRefreshToken = await _authService.GenerateRefreshToken(user.Id);

                _logger.LogInformation("Generated new refresh token: {RefreshToken} => expires {Expires}",
                    newRefreshToken.Token, newAccessToken.ValidTo);

                return Ok(new
                {
                    accessToken = new JwtSecurityTokenHandler().WriteToken(newAccessToken),
                    refreshToken = newRefreshToken.Token,
                    expires = newAccessToken.ValidTo
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Refresh token failed unexpectedly.");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("debug-auth")]
        public IActionResult DebugAuth()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return Unauthorized(new { message = "User not authenticated", 
                                        claims = User.Claims.Select(c => new { c.Type, c.Value }) });
            }
            
            var claims = User.Claims.ToDictionary(c => c.Type, c => c.Value);
            var authType = User.Identity.AuthenticationType;
            
            return Ok(new { 
                message = "Authentication successful", 
                authType,
                userId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                userName = User.FindFirstValue(ClaimTypes.Name),
                role = User.FindFirstValue(ClaimTypes.Role),
                claims
            });
        }
    }

    public class UserRegistrationDto
    {
        [Required] public string Username { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, MinLength(8)] public string Password { get; set; } = string.Empty;
    }

    public class UserLoginDto
    {
        [Required] public string Username { get; set; } = string.Empty;
        [Required] public string Password { get; set; } = string.Empty;
    }

    public class RefreshTokenRequest
    {
        [Required] public string AccessToken { get; set; } = string.Empty;
        [Required] public string RefreshToken { get; set; } = string.Empty;
    }
}