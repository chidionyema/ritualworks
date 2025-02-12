using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using haworks.Controllers;
using haworks.Models;
using haworks.Dto;
using haworks.Db;
using Moq;
using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Security.Cryptography;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;

namespace haworks.UnitTests.Controllers
{
    public class AuthenticationControllerTests
    {
        private readonly Mock<UserManager<User>> _userManagerMock;
        private readonly Mock<IConfiguration> _configurationMock;
        private readonly Mock<ILogger<AuthenticationController>> _loggerMock;
        private readonly Mock<haworksContext> _contextMock;
        private readonly AuthenticationController _controller;

        public AuthenticationControllerTests()
        {
            _userManagerMock = new Mock<UserManager<User>>(MockBehavior.Strict); // Strict mock to ensure all setups are defined
            _configurationMock = new Mock<IConfiguration>(MockBehavior.Strict);
            _loggerMock = new Mock<ILogger<AuthenticationController>>(MockBehavior.Strict);
            _contextMock = new Mock<haworksContext>(); // MockBehavior.Loose for DbContext - can be refined if needed

            // Setup Configuration Mock - Minimal for JWT settings
            _configurationMock.Setup(config => config["Jwt:Issuer"]).Returns("testIssuer");
            _configurationMock.Setup(config => config["Jwt:Audience"]).Returns("testAudience");
            _configurationMock.Setup(config => config["Jwt:PrivateKey"]).Returns(GenerateTestPrivateKey()); // Implement GenerateTestPrivateKey
            _configurationMock.Setup(config => config["ASPNETCORE_ENVIRONMENT"]).Returns("Development"); //Default environment

            _controller = new AuthenticationController(_userManagerMock.Object, _configurationMock.Object, _loggerMock.Object, _contextMock.Object);

            //Mock ControllerContext for HttpContext Access (Cookie setting etc.)
            _controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext()
            };
        }

        private string GenerateTestPrivateKey()
        {
            using var rsa = RSA.Create();
            return rsa.ExportToPem();
        }


        #region Register Tests

        [Fact]
        public async Task Register_ValidInput_ReturnsOk()
        {
            // Arrange
            var registrationDto = new UserRegistrationDto { Username = "testuser", Email = "test@example.com", Password = "Password123!" };
            var user = new User { UserName = registrationDto.Username, Email = registrationDto.Email };
            IdentityResult successResult = IdentityResult.Success;

            _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<User>(), registrationDto.Password)).ReturnsAsync(successResult);
            _loggerMock.Setup(logger => logger.LogInformation(It.IsAny<string>(), It.IsAny<object[]>())); // Expect info log for success

            // Act
            var result = await _controller.Register(registrationDto);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            dynamic responseObject = okResult.Value!;
            Assert.NotNull(responseObject.Token);
            Assert.NotNull(responseObject.UserId);
            Assert.NotNull(responseObject.Expires);
            Assert.Equal(200, okResult.StatusCode);
        }


        [Fact]
        public async Task Register_InvalidInput_ReturnsBadRequest()
        {
            // Arrange
            var registrationDto = new UserRegistrationDto { Username = null, Email = "invalid-email", Password = "short" }; // Invalid DTO
            _controller.ModelState.AddModelError("Username", "Username is required");
            _controller.ModelState.AddModelError("Email", "Invalid email format");
            _controller.ModelState.AddModelError("Password", "Password must be at least 8 characters");

            _loggerMock.Setup(logger => logger.LogWarning(It.IsAny<string>(), It.IsAny<object[]>())); // Expect warning log for invalid input

            // Act
            var result = await _controller.Register(registrationDto);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<SerializableError>(badRequestResult.Value);
            Assert.Equal(400, badRequestResult.StatusCode);
        }


        [Fact]
        public async Task Register_UserManagerFails_ReturnsBadRequestWithIdentityErrors()
        {
            // Arrange
            var registrationDto = new UserRegistrationDto { Username = "testuser", Email = "test@example.com", Password = "Password123!" };
            IdentityResult errorResult = IdentityResult.Failed(new IdentityError { Description = "Error creating user" });

            _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<User>(), registrationDto.Password)).ReturnsAsync(errorResult);
            _loggerMock.Setup(logger => logger.LogWarning(It.IsAny<string>(), It.IsAny<object[]>())); // Expect warning log for registration failure

            // Act
            var result = await _controller.Register(registrationDto);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsAssignableFrom<object[]>(badRequestResult.Value); // IdentityError[] is boxed as object[]
            Assert.Equal(400, badRequestResult.StatusCode);
        }

        #endregion

        #region Login Tests

        [Fact]
        public async Task Login_ValidCredentials_ReturnsOkWithTokenUserAndCookie()
        {
            // Arrange
            var loginDto = new UserLoginDto { Username = "testuser", Password = "Password123!" };
            var user = new User { Id = "testuserid", UserName = loginDto.Username, Email = "test@example.com" };

            _userManagerMock.Setup(um => um.FindByNameAsync(loginDto.Username)).ReturnsAsync(user);
            _userManagerMock.Setup(um => um.CheckPasswordAsync(user, loginDto.Password)).ReturnsAsync(true);

            _contextMock.Setup(ctx => ctx.Subscriptions.AnyAsync(It.Is<System.Linq.Expressions.Expression<Func<Subscription, bool>>>(e => e.Compile()(new Subscription() { UserId = user.Id, Status = SubscriptionStatus.Active })) , default)).ReturnsAsync(true); // Mock subscription status as active

            _loggerMock.Setup(logger => logger.LogInformation(It.IsAny<string>(), It.IsAny<object[]>())); // Expect info log for successful login

            // Act
            var result = await _controller.Login(loginDto);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            dynamic responseObject = okResult.Value!;
            Assert.NotNull(responseObject.Token);
            Assert.NotNull(responseObject.User);
            Assert.NotNull(responseObject.Expires);
            Assert.Equal(user.Id, responseObject.User.Id);
            Assert.Equal(user.UserName, responseObject.User.UserName);
            Assert.Equal(user.Email, responseObject.User.Email);
            Assert.True(responseObject.User.isSubscribed); // Verify subscription status is returned
            Assert.Equal(200, okResult.StatusCode);

            var cookie = _controller.ControllerContext.HttpContext.Response.Cookies.SingleOrDefault(c => c.Key == "jwt");
            Assert.NotNull(cookie); // Check JWT cookie is set
            Assert.True(cookie.Value.Options.HttpOnly);
            Assert.True(cookie.Value.Options.Secure);
            Assert.Equal(SameSiteMode.Strict, cookie.Value.Options.SameSite);

        }


        [Fact]
        public async Task Login_InvalidInput_ReturnsBadRequest()
        {
            // Arrange
            var loginDto = new UserLoginDto { Username = null, Password = "short" };
            _controller.ModelState.AddModelError("Username", "Username is required");
            _controller.ModelState.AddModelError("Password", "Password is required");
            _loggerMock.Setup(logger => logger.LogWarning(It.IsAny<string>(), It.IsAny<object[]>())); // Expect warning log for invalid input

            // Act
            var result = await _controller.Login(loginDto);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<SerializableError>(badRequestResult.Value);
            Assert.Equal(400, badRequestResult.StatusCode);
        }

        [Fact]
        public async Task Login_InvalidCredentials_ReturnsUnauthorizedWithJsonError()
        {
            // Arrange
            var loginDto = new UserLoginDto { Username = "testuser", Password = "wrongpassword" };
            User? nullUser = null; // Simulate user not found

            _userManagerMock.Setup(um => um.FindByNameAsync(loginDto.Username)).ReturnsAsync(nullUser); // User not found
            _loggerMock.Setup(logger => logger.LogWarning(It.IsAny<string>(), It.IsAny<object[]>())); // Expect warning log for failed login

            // Act
            var result = await _controller.Login(loginDto);

            // Assert
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            dynamic responseObject = unauthorizedResult.Value!;
            Assert.Equal("Invalid credentials", responseObject.message); // Check JSON error message
            Assert.Equal(401, unauthorizedResult.StatusCode);
        }


        #endregion

        #region Logout Tests

        [Fact]
        public void Logout_ReturnsOkAndDeletesCookie()
        {
            // Arrange
            _loggerMock.Setup(logger => logger.LogInformation(It.IsAny<string>(), It.IsAny<object[]>())); // Expect info log for logout

            // Act
            var result = _controller.Logout();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            dynamic responseObject = okResult.Value!;
            Assert.Equal("Logged out successfully", responseObject.Message);
            Assert.Equal(200, okResult.StatusCode);
            var cookie = _controller.ControllerContext.HttpContext.Response.Cookies.SingleOrDefault(c => c.Key == "jwt");
            Assert.Null(cookie); // Check JWT cookie is deleted
        }

        #endregion

        #region VerifyToken Tests

        [Fact]
        public void VerifyToken_ValidTokenInHeader_ReturnsOkWithUserInfo()
        {
            // Arrange
            var userName = "testuser";
            var userId = "testuserid";
            var tokenString = GenerateTestToken(userName, userId);

            _controller.ControllerContext.HttpContext.Request.Headers["Authorization"] = $"Bearer {tokenString}";
            _loggerMock.Setup(logger => logger.LogDebug(It.IsAny<string>(), It.IsAny<object[]>())); // Expect debug log for token verification

            // Act
            var result = _controller.VerifyToken();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            dynamic responseObject = okResult.Value!;
            Assert.Equal(userName, responseObject.UserName);
            Assert.Equal(userId, responseObject.UserId);
            Assert.Equal(200, okResult.StatusCode);
        }

        [Fact]
        public void VerifyToken_NoTokenInHeader_ReturnsUnauthorizedWithJsonError()
        {
            // Arrange
            _loggerMock.Setup(logger => logger.LogWarning(It.IsAny<string>(), It.IsAny<object[]>())); // Expect warning log for no token

            // Act
            var result = _controller.VerifyToken();

            // Assert
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            dynamic responseObject = unauthorizedResult.Value!;
            Assert.Equal("Invalid token", responseObject.message); // Check JSON error message
            Assert.Equal(401, unauthorizedResult.StatusCode);
        }

        [Fact]
        public void VerifyToken_InvalidToken_ReturnsUnauthorizedWithJsonError()
        {
            // Arrange
            var invalidToken = "invalid.jwt.token";
            _controller.ControllerContext.HttpContext.Request.Headers["Authorization"] = $"Bearer {invalidToken}";
            _loggerMock.Setup(logger => logger.LogWarning(It.IsAny<string>(), It.IsAny<object[]>())); // Expect warning log for invalid token

            // Act
            var result = _controller.VerifyToken();

            // Assert
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            dynamic responseObject = unauthorizedResult.Value!;
            Assert.Equal("Token validation failed", responseObject.message); // Check JSON error message
            Assert.Equal(401, unauthorizedResult.StatusCode);
        }


        #endregion

        #region RefreshToken Tests

        [Fact]
        public async Task RefreshToken_ValidTokens_ReturnsOkWithNewTokens()
        {
            // Arrange
            var accessToken = GenerateTestToken("testuser", "testuserid");
            var refreshTokenString = "validRefreshToken";
            var refreshTokenEntity = new RefreshToken { UserId = "testuserid", Token = refreshTokenString, Expires = DateTime.UtcNow.AddDays(1) };
            var refreshTokenRequest = new RefreshTokenRequest { AccessToken = accessToken, RefreshToken = refreshTokenString };

            _contextMock.Setup(ctx => ctx.RefreshTokens.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>(), default)).ReturnsAsync(refreshTokenEntity);
            _contextMock.Setup(ctx => ctx.RefreshTokens.Remove(refreshTokenEntity));
            _contextMock.Setup(ctx => ctx.SaveChangesAsync(default)).ReturnsAsync(1); // Simulate successful SaveChanges

            _loggerMock.Setup(logger => logger.LogInformation(It.IsAny<string>(), It.IsAny<object[]>())); // Expect info log for token refresh

            // Act
            var result = await _controller.RefreshToken(refreshTokenRequest);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            dynamic responseObject = okResult.Value!;
            Assert.NotNull(responseObject.AccessToken);
            Assert.NotNull(responseObject.RefreshToken);
            Assert.NotNull(responseObject.Expires);
            Assert.Equal(200, okResult.StatusCode);
            _contextMock.Verify(ctx => ctx.RefreshTokens.Remove(refreshTokenEntity), Times.Once); // Verify refresh token is removed
            _contextMock.Verify(ctx => ctx.SaveChangesAsync(default), Times.Once); // Verify SaveChanges is called

        }


        [Fact]
        public async Task RefreshToken_InvalidAccessToken_ReturnsUnauthorizedWithJsonError()
        {
            // Arrange
            var invalidAccessToken = "invalid.access.token";
            var refreshTokenRequest = new RefreshTokenRequest { AccessToken = invalidAccessToken, RefreshToken = "refreshToken" };
            _loggerMock.Setup(logger => logger.LogWarning(It.IsAny<string>(), It.IsAny<object[]>())); // Expect warning log for invalid access token

            // Act
            var result = await _controller.RefreshToken(refreshTokenRequest);

            // Assert
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            dynamic responseObject = unauthorizedResult.Value!;
            Assert.Equal("Invalid token", responseObject.message); // Check JSON error message
            Assert.Equal(401, unauthorizedResult.StatusCode);
        }


        [Fact]
        public async Task RefreshToken_InvalidRefreshToken_ReturnsUnauthorizedWithJsonError()
        {
            // Arrange
            var accessToken = GenerateTestToken("testuser", "testuserid");
            var refreshTokenRequest = new RefreshTokenRequest { AccessToken = accessToken, RefreshToken = "invalidRefreshToken" };
            RefreshToken? nullRefreshToken = null; // Simulate refresh token not found

            _contextMock.Setup(ctx => ctx.RefreshTokens.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<RefreshToken, bool>>>(), default)).ReturnsAsync(nullRefreshToken);
            _loggerMock.Setup(logger => logger.LogWarning(It.IsAny<string>(), It.IsAny<object[]>())); // Expect warning log for invalid refresh token

            // Act
            var result = await _controller.RefreshToken(refreshTokenRequest);

            // Assert
            var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
            dynamic responseObject = unauthorizedResult.Value!;
            Assert.Equal("Invalid refresh token", responseObject.message); // Check JSON error message
            Assert.Equal(401, unauthorizedResult.StatusCode);
        }


        #endregion

        #region Microsoft External Login Tests

        [Fact]
        public void LoginMicrosoft_ChallengeResult()
        {
            // Arrange
            var redirectUrl = "/signin-microsoft"; // Expected Redirect URI from Challenge
            var mockUrlHelper = new Mock<IUrlHelper>();
            mockUrlHelper.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns(redirectUrl);
            _controller.Url = mockUrlHelper.Object;

            // Act
            var result = _controller.LoginMicrosoft();

            // Assert
            var challengeResult = Assert.IsType<ChallengeResult>(result);
            Assert.Equal(MicrosoftAccountDefaults.AuthenticationScheme, challengeResult.AuthenticationSchemes.First());
            Assert.Equal(redirectUrl, challengeResult.Properties.RedirectUri);
            Assert.Equal(MicrosoftAccountDefaults.AuthenticationScheme, challengeResult.Properties.Items["scheme"]); // Check Scheme in Properties

        }


        [Fact]
        public async Task MicrosoftCallback_SuccessfulAuthExistingUser_ReturnsOkWithTokensUserAndCookie()
        {
            // Arrange
            var email = "existinguser@example.com";
            var user = new User { Id = "existinguserid", UserName = "ExistingUser", Email = email };
            var authResultMock = new Mock<AuthenticateResult>();
            authResultMock.Setup(ar => ar.Succeeded).Returns(true);
            authResultMock.Setup(ar => ar.Principal).Returns(new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim(ClaimTypes.Email, email)
            }, MicrosoftAccountDefaults.AuthenticationScheme)));


            _userManagerMock.Setup(um => um.FindByEmailAsync(email)).ReturnsAsync(user);
            _contextMock.Setup(ctx => ctx.Subscriptions.AnyAsync(It.Is<System.Linq.Expressions.Expression<Func<Subscription, bool>>>(e => e.Compile()(new Subscription() { UserId = user.Id, Status = SubscriptionStatus.Active })), default)).ReturnsAsync(true); // Mock subscription status active
            _loggerMock.Setup(logger => logger.LogInformation(It.IsAny<string>(), It.IsAny<object[]>())); // Expect info log for successful external login

            _controller.ControllerContext.HttpContext.RequestServices = Mock.Of<IServiceProvider>(s =>
                Mock.Get(s).Setup(provider => provider.GetService(typeof(IAuthenticationService)))
                           .Returns(Mock.Of<IAuthenticationService>(authServiceMock =>
                                        Mock.Get(authServiceMock).Setup(auth => authServiceMock.AuthenticateAsync(It.IsAny<HttpContext>(), MicrosoftAccountDefaults.AuthenticationScheme))
                                        .ReturnsAsync(authResultMock.Object)
                                    )
                           ).Object
            );


            // Act
            var result = await _controller.MicrosoftCallback();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            dynamic responseObject = okResult.Value!;
            Assert.NotNull(responseObject.AccessToken);
            Assert.NotNull(responseObject.RefreshToken);
            Assert.NotNull(responseObject.Expires);
            Assert.NotNull(responseObject.User);
            Assert.Equal(user.Id, responseObject.User.Id);
            Assert.Equal(user.UserName, responseObject.User.UserName);
            Assert.Equal(user.Email, responseObject.User.Email);
            Assert.True(responseObject.User.isSubscribed); // Verify subscription status is returned
            Assert.Equal(200, okResult.StatusCode);

            var cookie = _controller.ControllerContext.HttpContext.Response.Cookies.SingleOrDefault(c => c.Key == "jwt");
            Assert.NotNull(cookie); // Check JWT cookie is set

        }

        [Fact]
        public async Task MicrosoftCallback_SuccessfulAuthNewUser_ReturnsOkWithTokensUserAndCookie()
        {
            // Arrange
            var email = "newuser@example.com";
            User? nullUser = null; // Simulate user not found initially
            var newUser = new User { Id = "newuserid", UserName = email, Email = email };
            var identityResultSuccess = IdentityResult.Success;


            var authResultMock = new Mock<AuthenticateResult>();
            authResultMock.Setup(ar => ar.Succeeded).Returns(true);
            authResultMock.Setup(ar => ar.Principal).Returns(new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim(ClaimTypes.Email, email)
            }, MicrosoftAccountDefaults.AuthenticationScheme)));


            _userManagerMock.Setup(um => um.FindByEmailAsync(email)).ReturnsAsync(nullUser); // User not found initially
            _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<User>())).ReturnsAsync(identityResultSuccess); // Simulate successful user creation
            _contextMock.Setup(ctx => ctx.Subscriptions.AnyAsync(It.Is<System.Linq.Expressions.Expression<Func<Subscription, bool>>>(e => e.Compile()(new Subscription() { UserId = newUser.Id, Status = SubscriptionStatus.Active })), default)).ReturnsAsync(false); // Mock subscription status inactive
            _loggerMock.Setup(logger => logger.LogInformation(It.IsAny<string>(), It.IsAny<object[]>())); // Expect info log for successful external login

            _controller.ControllerContext.HttpContext.RequestServices = Mock.Of<IServiceProvider>(s =>
                Mock.Get(s).Setup(provider => provider.GetService(typeof(IAuthenticationService)))
                           .Returns(Mock.Of<IAuthenticationService>(authServiceMock =>
                                        Mock.Get(authServiceMock).Setup(auth => authServiceMock.AuthenticateAsync(It.IsAny<HttpContext>(), MicrosoftAccountDefaults.AuthenticationScheme))
                                        .ReturnsAsync(authResultMock.Object)
                                    )
                           ).Object
            );

            // Act
            var result = await _controller.MicrosoftCallback();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            dynamic responseObject = okResult.Value!;
            Assert.NotNull(responseObject.AccessToken);
            Assert.NotNull(responseObject.RefreshToken);
            Assert.NotNull(responseObject.Expires);
            Assert.NotNull(responseObject.User);
            Assert.Equal(newUser.Id, responseObject.User.Id);
            Assert.Equal(newUser.UserName, responseObject.User.UserName);
            Assert.Equal(newUser.Email, responseObject.User.Email);
            Assert.False(responseObject.User.isSubscribed); // Verify subscription status is returned
            Assert.Equal(200, okResult.StatusCode);

            var cookie = _controller.ControllerContext.HttpContext.Response.Cookies.SingleOrDefault(c => c.Key == "jwt");
            Assert.NotNull(cookie); // Check JWT cookie is set
        }


        [Fact]
        public async Task MicrosoftCallback_ExternalAuthFails_ReturnsBadRequestWithJsonError()
        {
            // Arrange
            var authResultMock = new Mock<AuthenticateResult>();
            authResultMock.Setup(ar => ar.Succeeded).Returns(false);
            authResultMock.Setup(ar => ar.Failure).Returns(new Exception("External auth failed"));
            _loggerMock.Setup(logger => logger.LogWarning(It.IsAny<string>(), It.IsAny<object[]>())); // Expect warning log for failed external auth

            _controller.ControllerContext.HttpContext.RequestServices = Mock.Of<IServiceProvider>(s =>
               Mock.Get(s).Setup(provider => provider.GetService(typeof(IAuthenticationService)))
                          .Returns(Mock.Of<IAuthenticationService>(authServiceMock =>
                                       Mock.Get(authServiceMock).Setup(auth => authServiceMock.AuthenticateAsync(It.IsAny<HttpContext>(), MicrosoftAccountDefaults.AuthenticationScheme))
                                       .ReturnsAsync(authResultMock.Object)
                                   )
                          ).Object
           );


            // Act
            var result = await _controller.MicrosoftCallback();

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            dynamic responseObject = badRequestResult.Value!;
            Assert.Equal("External authentication failed", responseObject.message); // Check JSON error message
            Assert.Equal(400, badRequestResult.StatusCode);
        }


        #endregion


        private string GenerateTestToken(string userName, string userId)
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(_configurationMock.Object["Jwt:PrivateKey"]);
            var rsaSecurityKey = new RsaSecurityKey(rsa);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(JwtRegisteredClaimNames.Sub, userName),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                    new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
                }),
                Expires = DateTime.UtcNow.AddMinutes(15),
                Issuer = _configurationMock.Object["Jwt:Issuer"],
                Audience = _configurationMock.Object["Jwt:Audience"],
                SigningCredentials = new SigningCredentials(rsaSecurityKey, SecurityAlgorithms.RsaSsaPssSha256)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }


    }
}