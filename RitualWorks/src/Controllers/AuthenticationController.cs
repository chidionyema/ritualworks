using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.IdentityModel.Tokens.Jwt;
using System;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using RitualWorks.Db;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace RitualWorks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthenticationController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthenticationController> _logger;

        public AuthenticationController(UserManager<User> userManager, IConfiguration configuration, ILogger<AuthenticationController> logger)
        {
            _userManager = userManager;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserRegistrationDto registrationDto)
        {
            if (registrationDto == null || string.IsNullOrEmpty(registrationDto.Password))
            {
                return BadRequest("Invalid registration details");
            }

            var user = new User
            {
                UserName = registrationDto.Username,
                Email = registrationDto.Email
            };

            var result = await _userManager.CreateAsync(user, registrationDto.Password);

            if (!result.Succeeded)
            {
                return BadRequest(result.Errors);
            }

            var token = GenerateToken(user.UserName, user.Id, DateTime.Now.AddHours(3));
            SetJwtCookie(token);

            return Ok(new { message = "User registered successfully", token = new JwtSecurityTokenHandler().WriteToken(token), userId = user.Id });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginDto loginDto)
        {
            if (loginDto == null || string.IsNullOrEmpty(loginDto.Password))
            {
                return BadRequest("Invalid login details");
            }

            var user = await _userManager.FindByNameAsync(loginDto.Username);
            if (user == null || !await _userManager.CheckPasswordAsync(user, loginDto.Password))
            {
                return Unauthorized("Invalid username or password");
            }

            var token = GenerateToken(user.UserName, user.Id, DateTime.Now.AddHours(1));
            SetJwtCookie(token);

            var userResponse = new
            {
                id = user.Id,
                username = user.UserName,
                email = user.Email
            };

            return Ok(new { message = "Login successful", user = userResponse, token = new JwtSecurityTokenHandler().WriteToken(token) });
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("jwt");
            return Ok(new { message = "Logged out successfully" });
        }

        [HttpGet("verify-token")]
        public IActionResult VerifyToken()
        {
        // Retrieve the token from the 'Authorization' header
        var authHeader = Request.Headers["Authorization"].ToString();

        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return Unauthorized("Token is missing or malformed");
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();

        try
        {
            var principal = GetPrincipalFromToken(token);
            if (principal == null)
            {
                return Unauthorized("Invalid token");
            }

            var userName = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Ok(new { userName, userId });
        }
        catch (SecurityTokenException)
        {
            return Unauthorized("Invalid token");
        }
    }


        [HttpPost("refresh-token")]
        public IActionResult RefreshToken()
        {
            var authHeader = Request.Headers["Authorization"].ToString();

            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return Unauthorized("Token is missing or malformed");
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            if (string.IsNullOrEmpty(token))
            {
                return Unauthorized("Token is missing");
            }

            var principal = GetPrincipalFromExpiredToken(token);
            var newToken = GenerateToken(principal.Claims, DateTime.UtcNow.AddHours(1));
            SetJwtCookie(newToken);

            return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(newToken) });
        }

        private ClaimsPrincipal GetPrincipalFromToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidAudience = _configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!))
            }, out var validatedToken);

            return principal;
        }

        private ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = false, // Here we are saying that we don't care about the token's expiration date
                ValidateIssuerSigningKey = true,
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidAudience = _configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!))
            }, out var validatedToken);

            return principal;
        }

        private JwtSecurityToken GenerateToken(string? userName, string? userId, DateTime expiration, List<Claim>? additionalClaims = null)
        {
            var authClaims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, userName ?? string.Empty),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            if (!string.IsNullOrEmpty(userId))
            {
                authClaims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            }

            if (additionalClaims != null)
            {
                authClaims.AddRange(additionalClaims);
            }

            var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                expires: expiration,
                claims: authClaims,
                signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
            );

            return token;
        }

        private JwtSecurityToken GenerateToken(IEnumerable<Claim> claims, DateTime expiration)
        {
            var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: expiration,
                signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
            );

            return token;
        }

        private void SetJwtCookie(JwtSecurityToken token)
        {
            bool isDevelopment = _configuration["ASPNETCORE_ENVIRONMENT"]?.ToLower() == "development";

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = !isDevelopment,
                SameSite = SameSiteMode.None,
                Expires = token.ValidTo
            };
            Response.Cookies.Append("jwt", tokenString, cookieOptions);
        }

        public class UserRegistrationDto
        {
            public string Username { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public class UserLoginDto
        {
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }
    }
}
