using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using haworks.Db;
using haworks.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Http;
using Haworks.Infrastructure.Data;

namespace haworks.Services
{
    public class AuthService
    {
        private readonly UserManager<User> _userManager;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthService> _logger;
        private readonly IdentityContext _identityContext;
        private readonly SymmetricSecurityKey _securityKey;

        public AuthService(
            UserManager<User> userManager,
            IConfiguration config,
            ILogger<AuthService> logger,
            IdentityContext identityContext)
        {
            _userManager = userManager;
            _config = config;
            _logger = logger;
            _identityContext = identityContext;
            
            var keyBytes = Convert.FromBase64String(config["Jwt:Key"]!);
            _securityKey = new SymmetricSecurityKey(keyBytes);
        }

        public async Task<JwtSecurityToken> GenerateToken(User user, DateTime expiration)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.UserName!),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email!)
            };

            var roles = await _userManager.GetRolesAsync(user);
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            return new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: expiration,
                signingCredentials: new SigningCredentials(_securityKey, SecurityAlgorithms.HmacSha256)
            );
        }

        public async Task<RefreshToken> GenerateRefreshToken(string userId)
        {
            var newToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
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

        public async Task RevokeToken(string jti, string userId, DateTime expiryDate)
        {
            _identityContext.RevokedTokens.Add(new RevokedToken
            {
                Jti = jti,
                UserId = userId,
                ExpiryDate = expiryDate
            });
            await _identityContext.SaveChangesAsync();
        }

        public async Task<bool> IsTokenRevoked(string jti)
        {
            return await _identityContext.RevokedTokens
                .AnyAsync(rt => rt.Jti == jti && rt.ExpiryDate > DateTime.UtcNow);
        }

        public void SetSecureCookie(HttpContext context, JwtSecurityToken token)
        {
            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            context.Response.Cookies.Append("jwt", tokenString, new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = token.ValidTo,
                Path = "/"
            });
        }

        public void DeleteAuthCookie(HttpContext context)
        {
            context.Response.Cookies.Delete("jwt", new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
        }

        public TokenValidationParameters GetTokenValidationParameters()
        {
            return new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _securityKey,
                ValidateIssuer = true,
                ValidIssuer = _config["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _config["Jwt:Audience"],
                ValidateLifetime = true,
                LifetimeValidator = (notBefore, expires, securityToken, validationParameters) =>
                {
                    if (expires < DateTime.UtcNow) return false;
                    
                    var jwtToken = (JwtSecurityToken)securityToken;
                    var jti = jwtToken.Id;
                    return !IsTokenRevoked(jti).GetAwaiter().GetResult();
                }
            };
        }

        public ClaimsPrincipal? ValidateToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            try
            {
                var validationParameters = GetTokenValidationParameters();
                return tokenHandler.ValidateToken(token, validationParameters, out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token validation failed");
                return null;
            }
        }
    }
}