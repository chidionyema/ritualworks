using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using haworks.Controllers;
using haworks.Db;
using haworks.DTOs;
using Xunit;
using UserRegistrationDto = haworks.Controllers.UserRegistrationDto;
using UserLoginDto = haworks.Controllers.UserLoginDto;
using Microsoft.Extensions.Logging;

public class AuthenticationControllerTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IConfiguration> _configurationMock;

    private readonly Mock<ILogger<AuthenticationController>> _loggerMock;
    private readonly AuthenticationController _authenticationController;

    public AuthenticationControllerTests()
    {
        _userManagerMock = new Mock<UserManager<User>>(
            Mock.Of<IUserStore<User>>(), null, null, null, null, null, null, null, null);

        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<AuthenticationController>>();
        _configurationMock.SetupGet(x => x["Jwt:Key"]).Returns("your-128-bit-secret-key-here12345678");
        _configurationMock.SetupGet(x => x["Jwt:Issuer"]).Returns("http://localhost:5000");
        _configurationMock.SetupGet(x => x["Jwt:Audience"]).Returns("your-audience");

        _authenticationController = new AuthenticationController(_userManagerMock.Object, _configurationMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Register_ShouldReturnBadRequest_WhenRegistrationDtoIsNull()
    {
        // Act
        var result = await _authenticationController.Register(null);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid registration details", badRequestResult.Value);
    }

    [Fact]
    public async Task Register_ShouldReturnBadRequest_WhenPasswordIsEmpty()
    {
        // Arrange
        var registrationDto = new UserRegistrationDto
        {
            Username = "testuser",
            Email = "testuser@example.com",
            Password = ""
        };

        // Act
        var result = await _authenticationController.Register(registrationDto);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid registration details", badRequestResult.Value);
    }

    [Fact]
    public async Task Register_ShouldReturnOk_WhenRegistrationIsSuccessful()
    {
        // Arrange
        var registrationDto = new haworks.Controllers.UserRegistrationDto
        {
            Username = "testuser",
            Email = "testuser@example.com",
            Password = "Test@123"
        };

        var user = new User
        {
            UserName = registrationDto.Username,
            Email = registrationDto.Email
        };

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<User>(), registrationDto.Password))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _authenticationController.Register(registrationDto);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("User registered successfully", okResult.Value);
    }

    [Fact]
    public async Task Register_ShouldReturnBadRequest_WhenRegistrationFails()
    {
        // Arrange
        var registrationDto = new UserRegistrationDto
        {
            Username = "testuser",
            Email = "testuser@example.com",
            Password = "Test@123"
        };

        var user = new User
        {
            UserName = registrationDto.Username,
            Email = registrationDto.Email
        };

        var identityErrors = new IdentityError[]
        {
            new IdentityError { Description = "Password is too weak" }
        };

        _userManagerMock.Setup(um => um.CreateAsync(It.IsAny<User>(), registrationDto.Password))
            .ReturnsAsync(IdentityResult.Failed(identityErrors));

        // Act
        var result = await _authenticationController.Register(registrationDto);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(identityErrors, badRequestResult.Value);
    }

    [Fact]
    public async Task Login_ShouldReturnBadRequest_WhenLoginDtoIsNull()
    {
        // Act
        var result = await _authenticationController.Login(null);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Invalid login details", badRequestResult.Value);
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_WhenUserNotFound()
    {
        // Arrange
        var loginDto = new UserLoginDto
        {
            Username = "testuser",
            Password = "Test@123"
        };

        _userManagerMock.Setup(um => um.FindByNameAsync(loginDto.Username))
            .ReturnsAsync((User)null);

        // Act
        var result = await _authenticationController.Login(loginDto);

        // Assert
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_WhenPasswordIsIncorrect()
    {
        // Arrange
        var loginDto = new UserLoginDto
        {
            Username = "testuser",
            Password = "Test@123"
        };

        var user = new User
        {
            UserName = loginDto.Username,
            Email = "testuser@example.com"
        };

        _userManagerMock.Setup(um => um.FindByNameAsync(loginDto.Username))
            .ReturnsAsync(user);

        _userManagerMock.Setup(um => um.CheckPasswordAsync(user, loginDto.Password))
            .ReturnsAsync(false);

        // Act
        var result = await _authenticationController.Login(loginDto);

        // Assert
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Login_ShouldReturnOk_WithToken_WhenCredentialsAreCorrect()
    {
        // Arrange
        var loginDto = new UserLoginDto
        {
            Username = "testuser",
            Password = "Test@123"
        };

        var user = new User
        {
            UserName = loginDto.Username,
            Email = "testuser@example.com"
        };

        _userManagerMock.Setup(um => um.FindByNameAsync(loginDto.Username))
            .ReturnsAsync(user);

        _userManagerMock.Setup(um => um.CheckPasswordAsync(user, loginDto.Password))
            .ReturnsAsync(true);

        _configurationMock.Setup(c => c["Jwt:Key"]).Returns("your-128-bit-secret-key-here12345678");
        _configurationMock.Setup(c => c["Jwt:Issuer"]).Returns("http://localhost:5000");
        _configurationMock.Setup(c => c["Jwt:Audience"]).Returns("your-audience");

        // Act
        var result = await _authenticationController.Login(loginDto);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var tokenResult = okResult.Value.GetType().GetProperty("token")?.GetValue(okResult.Value, null);
        var expirationResult = okResult.Value.GetType().GetProperty("expiration")?.GetValue(okResult.Value, null);

        Assert.NotNull(tokenResult);
        Assert.True(DateTime.UtcNow.AddHours(2) < (DateTime)expirationResult && (DateTime)expirationResult < DateTime.UtcNow.AddHours(4));
    }
}
