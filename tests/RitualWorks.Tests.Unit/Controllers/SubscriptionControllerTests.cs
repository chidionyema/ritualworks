using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using haworks.Controllers;
using haworks.Db;
using haworks.Dto;
using haworks.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Stripe;
using Stripe.Checkout;
using Xunit;

namespace haworks.Tests
{
    public class SubscriptionControllerTests : IDisposable
    {
        private readonly Mock<UserManager<User>> _userManagerMock;
        private readonly haworksContext _context;
        private readonly Mock<IStripeClient> _stripeClientMock;
        private readonly IConfiguration _configuration;
        private readonly Mock<ILogger<SubscriptionController>> _loggerMock;

        public SubscriptionControllerTests()
        {
            // Create an in-memory EF Core database.
            var options = new DbContextOptionsBuilder<haworksContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _context = new haworksContext(options);

            // Setup UserManager using a mocked IUserStore.
            var store = new Mock<IUserStore<User>>();
            _userManagerMock = new Mock<UserManager<User>>(store.Object, null, null, null, null, null, null, null, null);

            _stripeClientMock = new Mock<IStripeClient>();

            // Setup in-memory configuration.
            var inMemorySettings = new Dictionary<string, string>
            {
                {"Stripe:WebhookSecret", "whsec_test"},
                {"Frontend:BaseUrl", "https://frontend.test"},
                {"Stripe:MetadataSignatureSecret", "metasecret"},
                {"Stripe:AccountId", "acct_test"}
            };
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _loggerMock = new Mock<ILogger<SubscriptionController>>();
        }

        public void Dispose()
        {
            _context?.Dispose();
        }

        [Fact]
        public async Task GetSubscriptionStatus_ReturnsUnauthorized_WhenUserNotFound()
        {
            // Arrange: Setup UserManager.GetUserAsync to return null.
            _userManagerMock.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync((User)null);

            var controller = new SubscriptionController(
                _userManagerMock.Object, _context, _stripeClientMock.Object, _configuration, _loggerMock.Object);

            // Act
            var result = await controller.GetSubscriptionStatus();

            // Assert
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task GetSubscriptionStatus_ReturnsActiveStatus_WhenSubscriptionExists()
        {
            // Arrange: Create a fake user and subscription.
            var user = new User { Id = "user1", UserName = "testuser", Email = "test@example.com" };
            _userManagerMock.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(user);

            // Add a subscription record for the user.
            _context.Subscriptions.Add(new Subscription
            {
                UserId = user.Id,
                PlanId = "plan1",
                Status = SubscriptionStatus.Active,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
            });
            await _context.SaveChangesAsync();

            var controller = new SubscriptionController(
                _userManagerMock.Object, _context, _stripeClientMock.Object, _configuration, _loggerMock.Object);

            // Simulate a valid user identity in the HttpContext.
            var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, user.Id) };
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) }
            };

            // Act
            var result = await controller.GetSubscriptionStatus();
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<SubscriptionStatusResponseDto>(okResult.Value);

            // Assert
            Assert.True(response.IsSubscribed);
            Assert.Equal("plan1", response.PlanId);
        }

        [Fact]
        public async Task CreateCheckoutSession_ReturnsBadRequest_WhenModelStateInvalid()
        {
            // Arrange: Create controller with an invalid model state.
            var user = new User { Id = "user1", UserName = "testuser", Email = "test@example.com" };
            _userManagerMock.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(user);

            var controller = new SubscriptionController(
                _userManagerMock.Object, _context, _stripeClientMock.Object, _configuration, _loggerMock.Object);
            controller.ModelState.AddModelError("PriceId", "Required");

            // Act
            var result = await controller.CreateCheckoutSession(new SubscriptionRequest { PriceId = "", RedirectPath = "/valid" });

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task CreateCheckoutSession_ReturnsUnauthorized_WhenUserNotFound()
        {
            // Arrange: Setup UserManager.GetUserAsync to return null.
            _userManagerMock.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync((User)null);

            var controller = new SubscriptionController(
                _userManagerMock.Object, _context, _stripeClientMock.Object, _configuration, _loggerMock.Object);

            // Act
            var result = await controller.CreateCheckoutSession(new SubscriptionRequest { PriceId = "price_test", RedirectPath = "/valid" });

            // Assert
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task CreateCheckoutSession_ReturnsBadRequest_WhenInvalidPriceId()
        {
            // Arrange: Setup valid user.
            var user = new User { Id = "user1", UserName = "testuser", Email = "test@example.com" };
            _userManagerMock.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(user);

            // Ensure no subscription plan exists with the given PriceId.
            var controller = new SubscriptionController(
                _userManagerMock.Object, _context, _stripeClientMock.Object, _configuration, _loggerMock.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] { new Claim(ClaimTypes.NameIdentifier, user.Id) })) }
            };

            // Act
            var result = await controller.CreateCheckoutSession(new SubscriptionRequest { PriceId = "invalid_price", RedirectPath = "/valid" });

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid subscription plan.", badRequest.Value);
        }

        [Fact]
        public async Task CreateCheckoutSession_ReturnsBadRequest_WhenRedirectPathInvalid()
        {
            // Arrange: Setup valid user and subscription plan.
            var user = new User { Id = "user1", UserName = "testuser", Email = "test@example.com" };
            _userManagerMock.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(user);

            _context.SubscriptionPlans.Add(new SubscriptionPlan { Id = "plan1", StripePriceId = "price_test" });
            await _context.SaveChangesAsync();

            var controller = new SubscriptionController(
                _userManagerMock.Object, _context, _stripeClientMock.Object, _configuration, _loggerMock.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] { new Claim(ClaimTypes.NameIdentifier, user.Id) })) }
            };

            // Act: Use an absolute URI for RedirectPath (should be relative).
            var result = await controller.CreateCheckoutSession(new SubscriptionRequest { PriceId = "price_test", RedirectPath = "https://malicious.com" });

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task CreateCheckoutSession_ReturnsOk_WithSessionId()
        {
            // Arrange: Setup valid user and subscription plan.
            var user = new User { Id = "user1", UserName = "testuser", Email = "test@example.com" };
            _userManagerMock.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(user);

            _context.SubscriptionPlans.Add(new SubscriptionPlan { Id = "plan1", StripePriceId = "price_test" });
            await _context.SaveChangesAsync();

            // Arrange: Configure the mock _stripeClient to return a fake Session.
            var fakeSession = new Session { Id = "fake_session_id" };
            _stripeClientMock.Setup(client => client.RequestAsync<Session>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<RequestOptions>()))
                .ReturnsAsync(fakeSession);

            var controller = new SubscriptionController(
                _userManagerMock.Object, _context, _stripeClientMock.Object, _configuration, _loggerMock.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] { new Claim(ClaimTypes.NameIdentifier, user.Id) })) }
            };

            // Act
            var result = await controller.CreateCheckoutSession(new SubscriptionRequest { PriceId = "price_test", RedirectPath = "/valid" });

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var responseDto = Assert.IsType<CreateCheckoutSessionResponseDto>(okResult.Value);
            Assert.Equal("fake_session_id", responseDto.SessionId);
        }

        [Fact]
        public async Task HealthCheck_ReturnsOk_WhenHealthy()
        {
            // Arrange: For HealthCheck, the in-memory DB is healthy.
            // Setup _stripeClient mock to simulate a healthy Stripe API call.
            _stripeClientMock.Setup(client => client.RequestAsync<Account>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<RequestOptions>()))
                .ReturnsAsync(new Account { Id = "acct_test" });

            var controller = new SubscriptionController(
                _userManagerMock.Object, _context, _stripeClientMock.Object, _configuration, _loggerMock.Object);

            // Act
            var result = await controller.HealthCheck();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Healthy", okResult.Value);
        }

        [Fact]
        public async Task HealthCheck_Returns503_WhenUnhealthy()
        {
            // Arrange: Simulate an unhealthy DB by ensuring the database is deleted.
            _context.Database.EnsureDeleted();

            // Also simulate an unhealthy Stripe API by having the _stripeClient throw an exception.
            _stripeClientMock.Setup(client => client.RequestAsync<Account>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<RequestOptions>()))
                .ThrowsAsync(new StripeException("Test error"));

            var controller = new SubscriptionController(
                _userManagerMock.Object, _context, _stripeClientMock.Object, _configuration, _loggerMock.Object);

            // Act
            var result = await controller.HealthCheck();

            // Assert
            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusResult.StatusCode);
        }
    }
}
