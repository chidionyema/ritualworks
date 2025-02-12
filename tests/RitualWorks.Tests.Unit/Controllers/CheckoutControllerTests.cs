using haworks.Contracts;
using haworks.Controllers;
using haworks.Db;
using haworks.Dto;
using haworks.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;

namespace haworks.Tests.Controllers
{
    public class CheckoutControllerTests
    {
        private readonly Mock<ILogger<CheckoutController>> _loggerMock;
        private readonly Mock<IPaymentClient> _paymentClientMock;
        private readonly Mock<IOrderRepository> _orderRepositoryMock;
        private readonly Mock<IProductRepository> _productRepositoryMock;

        public CheckoutControllerTests()
        {
            _loggerMock = new Mock<ILogger<CheckoutController>>();
            _paymentClientMock = new Mock<IPaymentClient>();
            _orderRepositoryMock = new Mock<IOrderRepository>();
            _productRepositoryMock = new Mock<IProductRepository>();
        }

        [Fact]
        public async Task StartCheckout_NullRequest_ReturnsBadRequest()
        {
            // Arrange
            var controller = new CheckoutController(_loggerMock.Object, _paymentClientMock.Object, _orderRepositoryMock.Object, _productRepositoryMock.Object);

            // Act
            var result = await controller.StartCheckout(null);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("No items provided.", badRequestResult.Value);
        }

        [Fact]
        public async Task StartCheckout_EmptyItemsList_ReturnsBadRequest()
        {
            // Arrange
            var controller = new CheckoutController(_loggerMock.Object, _paymentClientMock.Object, _orderRepositoryMock.Object, _productRepositoryMock.Object);
            var request = new StartCheckoutRequest { Items = Array.Empty<CheckoutItem>() };

            // Act
            var result = await controller.StartCheckout(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("No items provided.", badRequestResult.Value);
        }

        [Fact]
        public async Task StartCheckout_ProductWithZeroQuantity_ReturnsBadRequest()
        {
            // Arrange
            var request = new StartCheckoutRequest
            {
                Items = new[]
                {
                    new CheckoutItem { ProductId = Guid.NewGuid(), Quantity = 0, UnitPrice = 10 }
                }
            };
            var controller = new CheckoutController(_loggerMock.Object, _paymentClientMock.Object, _orderRepositoryMock.Object, _productRepositoryMock.Object);

            // Act
            var result = await controller.StartCheckout(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("No items provided.", badRequestResult.Value);
        }

        [Fact]
        public async Task StartCheckout_ProductWithNegativeQuantity_ReturnsBadRequest()
        {
            // Arrange
            var request = new StartCheckoutRequest
            {
                Items = new[]
                {
                    new CheckoutItem { ProductId = Guid.NewGuid(), Quantity = -1, UnitPrice = 10 }
                }
            };
            var controller = new CheckoutController(_loggerMock.Object, _paymentClientMock.Object, _orderRepositoryMock.Object, _productRepositoryMock.Object);

            // Act
            var result = await controller.StartCheckout(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("No items provided.", badRequestResult.Value);
        }

        [Fact]
        public async Task StartCheckout_ExistingOrder_ReturnsOkWithOrderId()
        {
            // Arrange
            var request = new StartCheckoutRequest
            {
                Items = new[]
                {
                    new CheckoutItem { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 10 }
                }
            };
            var existingOrder = new Order(Guid.NewGuid(), 10, OrderStatus.Pending, "testuser");
            _orderRepositoryMock.Setup(repo => repo.BeginTransactionAsync()).ReturnsAsync(new Mock<IDbContextTransaction>().Object);
            _orderRepositoryMock.Setup(repo => repo.GetOrderByIdempotencyKeyAsync(It.IsAny<string>())).ReturnsAsync(existingOrder);
            var controller = new CheckoutController(_loggerMock.Object, _paymentClientMock.Object, _orderRepositoryMock.Object, _productRepositoryMock.Object);

            // Act
            var result = await controller.StartCheckout(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<dynamic>(okResult.Value);
            Assert.Equal(existingOrder.Id, response.orderId);
        }

        [Fact]
        public async Task StartCheckout_ProductNotFound_ReturnsBadRequest()
        {
            // Arrange
            var request = new StartCheckoutRequest
            {
                Items = new[]
                {
                    new CheckoutItem { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 10 }
                }
            };
            _orderRepositoryMock.Setup(repo => repo.BeginTransactionAsync()).ReturnsAsync(new Mock<IDbContextTransaction>().Object);
            _productRepositoryMock.Setup(repo => repo.GetProductByIdAsync(It.IsAny<Guid>())).ReturnsAsync(Task.FromResult<Product>(null));
            var controller = new CheckoutController(_loggerMock.Object, _paymentClientMock.Object, _orderRepositoryMock.Object, _productRepositoryMock.Object);

            // Act
            var result = await controller.StartCheckout(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.StartsWith("Product with ID", badRequestResult.Value.ToString());
        }

        [Fact]
        public async Task StartCheckout_InsufficientStock_ReturnsBadRequest()
        {
            // Arrange
            var request = new StartCheckoutRequest
            {
                Items = new[]
                {
                    new CheckoutItem { ProductId = Guid.NewGuid(), Quantity = 10, UnitPrice = 10 }
                }
            };
            var product = new Product { Id = request.Items[0].ProductId, Stock = 5, Name = "Test Product" };
            _orderRepositoryMock.Setup(repo => repo.BeginTransactionAsync()).ReturnsAsync(new Mock<IDbContextTransaction>().Object);
            _productRepositoryMock.Setup(repo => repo.GetProductByIdAsync(It.IsAny<Guid>())).ReturnsAsync(product);
            var controller = new CheckoutController(_loggerMock.Object, _paymentClientMock.Object, _orderRepositoryMock.Object, _productRepositoryMock.Object);

            // Act
            var result = await controller.StartCheckout(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Product Test Product is out of stock.", badRequestResult.Value);
        }

        [Fact]
        public async Task StartCheckout_PriceMismatch_ReturnsBadRequest()
        {
            // Arrange
            var request = new StartCheckoutRequest
            {
                Items = new[]
                {
                    new CheckoutItem { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 10 }
                }
            };
            var product = new Product { Id = request.Items[0].ProductId, Stock = 5, Name = "Test Product", UnitPrice = 12 };
            _orderRepositoryMock.Setup(repo => repo.BeginTransactionAsync()).ReturnsAsync(new Mock<IDbContextTransaction>().Object);
            _productRepositoryMock.Setup(repo => repo.GetProductByIdAsync(It.IsAny<Guid>())).ReturnsAsync(product);
            var controller = new CheckoutController(_loggerMock.Object, _paymentClientMock.Object, _orderRepositoryMock.Object, _productRepositoryMock.Object);

            // Act
            var result = await controller.StartCheckout(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Price of Test Product has changed. Please update your cart.", badRequestResult.Value);
        }

        [Fact]
        public async Task StartCheckout_ValidRequest_ReturnsOkWithClientSecret()
        {
            // Arrange
            var request = new StartCheckoutRequest
            {
                Items = new[]
                {
                    new CheckoutItem { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 10, Name = "Test Product 1" },
                    new CheckoutItem { ProductId = Guid.NewGuid(), Quantity = 2, UnitPrice = 15, Name = "Test Product 2" }
                }
            };
            var product1 = new Product { Id = request.Items[0].ProductId, Stock = 5, Name = "Test Product 1", UnitPrice = 10 };
            var product2 = new Product { Id = request.Items[1].ProductId, Stock = 10, Name = "Test Product 2", UnitPrice = 15 };
            var paymentIntentResponse = new CreatePaymentIntentResponse { ClientSecret = "test_client_secret" };

            _orderRepositoryMock.Setup(repo => repo.BeginTransactionAsync()).ReturnsAsync(new Mock<IDbContextTransaction>().Object);
            _productRepositoryMock.Setup(repo => repo.GetProductByIdAsync(request.Items[0].ProductId)).ReturnsAsync(product1);
            _productRepositoryMock.Setup(repo => repo.GetProductByIdAsync(request.Items[1].ProductId)).ReturnsAsync(product2);
            _paymentClientMock.Setup(client => client.CreatePaymentIntentAsync(It.IsAny<CreatePaymentIntentRequest>())).ReturnsAsync(paymentIntentResponse);
            var controller = new CheckoutController(_loggerMock.Object, _paymentClientMock.Object, _orderRepositoryMock.Object, _productRepositoryMock.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "testuser") }))
                }
            };

            // Act
            var result = await controller.StartCheckout(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<dynamic>(okResult.Value);
            Assert.Equal(paymentIntentResponse.ClientSecret, response.clientSecret);
        }

        [Fact]
        public async Task StartCheckout_DatabaseException_ReturnsInternalServerError()
        {
            // Arrange
            var request = new StartCheckoutRequest
            {
                Items = new[]
                {
                    new CheckoutItem { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 10 }
                }
            };
            _orderRepositoryMock.Setup(repo => repo.BeginTransactionAsync()).ThrowsAsync(new Exception("Database error"));
            var controller = new CheckoutController(_loggerMock.Object, _paymentClientMock.Object, _orderRepositoryMock.Object, _productRepositoryMock.Object);

            // Act
            var result = await controller.StartCheckout(request);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status500InternalServerError, statusCodeResult.StatusCode);
        }

        // ... (other test methods) ...
    }
}