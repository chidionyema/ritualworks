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
using System.Security.Cryptography;
using System.Threading.Tasks;
using haworks.Models;
using haworks.Dto;
using haworks.Db;
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
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthenticationController> _logger;
        private readonly IdentityContext _identityContext;
        private readonly SymmetricSecurityKey _symmetricSecurityKey;
        private readonly TokenValidationParameters _tokenValidationParameters;

        public AuthenticationController(
            UserManager<User> userManager,
            IConfiguration configuration,
            ILogger<AuthenticationController> logger,
            IdentityContext identityContext)
        {
            _userManager = userManager;
            _configuration = configuration;
            _logger = logger;
            _identityContext = identityContext;

            var keyString = configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(keyString))
            {
                throw new ArgumentException("JWT key is not configured.");
            }

            // Log the raw key length to ensure it's at least 32 bytes for HmacSha256
            var rawKey = Convert.FromBase64String(keyString);
            _logger.LogInformation("JWT raw key length (bytes): {Length}", rawKey.Length);

            var keyBytes = Convert.FromBase64String(keyString);
            _symmetricSecurityKey = new SymmetricSecurityKey(keyBytes);

            _tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero,
                IssuerSigningKey = _symmetricSecurityKey,
                // This setting ensures that the claim with type ClaimTypes.NameIdentifier
                // (set to the user ID in GenerateToken) is not overwritten by default mappings.
                NameClaimType = ClaimTypes.NameIdentifier
            };
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

            // 2) Add a custom claim "permission=upload_content"
            var claimResult = await _userManager.AddClaimAsync(
                user, new Claim("permission", "upload_content")
            );
            if (!claimResult.Succeeded)
                return BadRequest(claimResult.Errors);   

            // Use the modified async GenerateToken method passing the User object
            var token = await GenerateToken(user, DateTime.UtcNow.AddMinutes(15));
            SetSecureCookie(token);

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

            var token = await GenerateToken(user, DateTime.UtcNow.AddMinutes(15));
            SetSecureCookie(token);

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
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
        public IActionResult Logout()
        {
            _logger.LogInformation("Logout endpoint called; removing 'jwt' cookie.");
            Response.Cookies.Delete("jwt");
            return Ok(new { message = "Logged out successfully" });
        }

        [HttpGet("verify-token")]
        [Authorize]
        public async Task<IActionResult> VerifyToken()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("verify-token => No userId in Claims => returning Unauthorized");
                return Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("verify-token => userId not found in DB => returning Unauthorized");
                return Unauthorized();
            }

            _logger.LogInformation("verify-token => User found: {UserId}, {UserName}", user.Id, user.UserName);
            return Ok(new { userId = user.Id, userName = user.UserName });
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            _logger.LogInformation("RefreshToken called with AccessToken length: {AccessLen}, RefreshToken length: {RefreshLen}",
                request.AccessToken.Length, request.RefreshToken.Length);

            try
            {
                // Step 1: Validate the old Access Token
                var principal = ValidateToken(request.AccessToken);
                if (principal == null)
                {
                    _logger.LogWarning("RefreshToken => principal is null => returning Unauthorized");
                    return Unauthorized();
                }

                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                _logger.LogInformation("Attempting refresh for userId: {UserId}", userId);

                // Step 2: Confirm user still exists
                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("RefreshToken => user not found => returning Unauthorized");
                    return Unauthorized();
                }

                // Step 3: Validate the stored refresh token 
                var storedToken = await _identityContext.RefreshTokens
                    .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken && rt.UserId == userId);

                if (storedToken == null)
                {
                    _logger.LogWarning("RefreshToken => No matching refresh token in DB => Unauthorized");
                    return Unauthorized();
                }

                _logger.LogInformation("Found refresh token in DB: {RefreshToken}, expires: {Expires}, userId: {UserId}",
                    storedToken.Token, storedToken.Expires, storedToken.UserId);

                if (storedToken.Expires < DateTime.UtcNow)
                {
                    _logger.LogWarning("RefreshToken => storedToken expired on {ExpiredOn} => returning Unauthorized",
                        storedToken.Expires);
                    return Unauthorized();
                }

                // Step 4: Remove old refresh token from DB
                _logger.LogInformation("Removing old refresh token from DB: {RefreshToken}", storedToken.Token);
                _identityContext.RefreshTokens.Remove(storedToken);

                // Step 5: Save changes for removing old token 
                await _identityContext.SaveChangesAsync();
                _logger.LogInformation("Old refresh token removed successfully.");

                // Step 6: Generate new access token + new refresh token
                var newAccessToken = await GenerateToken(user, DateTime.UtcNow.AddMinutes(15));
                var newRefreshToken = await GenerateRefreshToken(user.Id);

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

        /// <summary>
        /// Asynchronously generates a JWT based on the provided user and expiration.
        /// </summary>
        private async Task<JwtSecurityToken> GenerateToken(User user, DateTime expiration)
        {
            var claims = new List<Claim>
            {        
                // Use user.Id as the subject and include user.UserName as unique name.
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(JwtRegisteredClaimNames.UniqueName, user.UserName),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            _logger.LogDebug("GenerateToken => userName: {UserName}, userId: {UserId}, expiration: {Expires}",
                user.UserName, user.Id, expiration);

            // Retrieve roles and claims from the user manager.
            var roles = await _userManager.GetRolesAsync(user);
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
            var dbClaims = await _userManager.GetClaimsAsync(user);
            claims.AddRange(dbClaims);

            return new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: expiration,
                signingCredentials: new SigningCredentials(_symmetricSecurityKey, SecurityAlgorithms.HmacSha256)
            );
        }

        private async Task<RefreshToken> GenerateRefreshToken(string userId)
        {
            var newToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            _logger.LogDebug("Generating new refresh token for userId: {UserId}, token: {Token}", userId, newToken);

            var refreshToken = new RefreshToken
            {
                UserId = userId,
                Token = newToken,
                Expires = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            };
            _identityContext.RefreshTokens.Add(refreshToken);
            await _identityContext.SaveChangesAsync();
            return refreshToken;
        }

        private ClaimsPrincipal? ValidateToken(string token)
        {
            _logger.LogDebug("Validating token: {Token}", token);
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var principal = handler.ValidateToken(token, _tokenValidationParameters, out var validToken);
                _logger.LogDebug("ValidateToken => validated principal => subject: {Subject}",
                    principal.Identity?.Name ?? "null");
                return principal;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ValidateToken => token validation failed => returning null principal");
                return null;
            }
        }

        private void SetSecureCookie(JwtSecurityToken token)
        {
            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            _logger.LogDebug("SetSecureCookie: Generated token string: {TokenString}", tokenString);
            var segments = tokenString.Split('.');
            _logger.LogDebug("SetSecureCookie: Token has {SegmentCount} segments.", segments.Length);
            _logger.LogDebug("SetSecureCookie => appending 'jwt' cookie with expiry: {Expires}", token.ValidTo);

            Response.Cookies.Append("jwt", tokenString, new CookieOptions
            {
                HttpOnly = true,
                Secure = HttpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = token.ValidTo
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
