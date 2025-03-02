using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using haworks.Db;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Haworks.Tests.Integration
{
    [ApiController]
    [Route("api/test-auth")]
    public class TestAuthController : ControllerBase
    {
        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<TestAuthController> _logger;
        private readonly haworks.Services.AuthService _authService;

        public TestAuthController(
            SignInManager<User> signInManager,
            UserManager<User> userManager,
            ILogger<TestAuthController> logger,
            haworks.Services.AuthService authService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _authService = authService;
        }

        [HttpGet("simulate-external-login")]
        public async Task<IActionResult> SimulateExternalLogin(
            [FromQuery] string provider,
            [FromQuery] string providerKey,
            [FromQuery] string name,
            [FromQuery] string email)
        {
            _logger.LogInformation("Simulating external login for provider: {Provider}, Key: {ProviderKey}, Name: {Name}, Email: {Email}",
                provider, providerKey, name, email);

            // Create claims for the external login
            var claims = new List<Claim>();

            if (!string.IsNullOrEmpty(name))
            {
                claims.Add(new Claim(ClaimTypes.Name, name));
            }

            if (!string.IsNullOrEmpty(email))
            {
                claims.Add(new Claim(ClaimTypes.Email, email));
            }

            claims.Add(new Claim(ClaimTypes.NameIdentifier, providerKey));

            // Create an identity and principal
            var identity = new ClaimsIdentity(claims, provider);
            var principal = new ClaimsPrincipal(identity);

            // Create the ExternalLoginInfo object that SignInManager expects
            var loginInfo = new ExternalLoginInfo(
                principal,
                provider,
                providerKey,
                provider);

            // Store the login info in the user's session
            HttpContext.Items["ExternalLoginInfo"] = loginInfo;
            
            // Override the GetExternalLoginInfoAsync method in the test environment
            // using a custom SignInManager extension method
            _signInManager.SetExternalLoginInfo(loginInfo);

            return Ok(new { Message = "External login simulated successfully" });
        }
        
        [HttpGet("generate-test-token")]
        public async Task<IActionResult> GenerateTestToken([FromQuery] string userId = null, [FromQuery] string userName = null)
        {
            // Create a test user if IDs aren't provided
            userId = userId ?? Guid.NewGuid().ToString();
            userName = userName ?? "test_user";
            
            var user = new User
            {
                Id = userId,
                UserName = userName,
                Email = $"{userName}@example.com"
            };

            // Generate a token using our AuthService
            var token = await _authService.GenerateToken(user, DateTime.UtcNow.AddHours(1));
            var tokenString = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
            
            // Set the cookie if needed
            _authService.SetSecureCookie(HttpContext, token);
            
            return Ok(new { 
                token = tokenString,
                userId = user.Id,
                userName = user.UserName,
                expires = token.ValidTo
            });
        }
    }
}