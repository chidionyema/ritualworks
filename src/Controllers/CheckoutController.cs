using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using haworks.Dto;
using haworks.Contracts;
using haworks.Db;
using haworks.Repositories;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore.Storage;

namespace haworks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CheckoutController : ControllerBase
    {
        private readonly ILogger<CheckoutController> _logger;
        private readonly IPaymentClient _paymentClient;
        private readonly IOrderRepository _orderRepository;
        private readonly IProductRepository _productRepository;

        public CheckoutController(
            ILogger<CheckoutController> logger,
            IPaymentClient paymentClient,
            IOrderRepository orderRepository,
            IProductRepository productRepository)
        {
            _logger = logger;
            _paymentClient = paymentClient;
            _orderRepository = orderRepository;
            _productRepository = productRepository;
        }

        [HttpPost("start")]
       // [EnableRateLimiting("DefaultPolicy")]
        public async Task<IActionResult> StartCheckout([FromBody] StartCheckoutRequest request)
        {
            if (request == null || request.Items?.Any() != true)
            {
                _logger.LogWarning("StartCheckout failed: No items provided.");
                return BadRequest(new { message = "No items provided." });
            }

            await using var transaction = await _orderRepository.BeginTransactionAsync();

            try
            {
                // Identify the user
                var userId = User.Identity?.IsAuthenticated == true
                    ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown"
                    : "guest";

                // Generate a unique idempotency key
                var idempotencyKey = GenerateIdempotencyKey(userId, request);

                // Check for an existing order with the same idempotency key
                var existingOrder = await _orderRepository.GetOrderByIdempotencyKeyAsync(idempotencyKey);
                if (existingOrder != null)
                {
                    _logger.LogInformation("Order already processed for IdempotencyKey: {IdempotencyKey}", idempotencyKey);

                    // Fetch the existing payment intent
                   // var existingPayment = await _paymentClient.GetPaymentIntentAsync(existingOrder.Id);
                    await transaction.CommitAsync();
                    return Ok(new
                    {
                        orderId = existingOrder.Id,
                       // clientSecret = existingPayment.ClientSecret
                    });
                }

                // Verify inventory and prices
                foreach (var item in request.Items)
                {
                    var product = await _productRepository.GetProductByIdAsync(item.ProductId);

                    if (product == null)
                    {
                        return BadRequest(new { message = $"Product with ID {item.ProductId} does not exist." });
                    }

                    if (product.Stock < item.Quantity)
                    {
                        return BadRequest(new { message = $"Product {product.Name} is out of stock." });
                    }

                    if (product.UnitPrice != item.UnitPrice)
                    {
                        return BadRequest(new { message = $"Price of {product.Name} has changed. Please update your cart." });
                    }

                    // Update stock with optimistic locking
                    await _productRepository.UpdateProductStockAsync(item.ProductId, item.Quantity);
                }

                // Calculate total amount
                var totalAmount = request.Items.Sum(i => i.UnitPrice * i.Quantity);

                // Create order
                var order = new Order(
                    id: Guid.NewGuid(),
                    totalAmount: totalAmount,
                    status: OrderStatus.Pending,
                    userId: userId)
                {
                    IdempotencyKey = idempotencyKey,
                    OrderItems = request.Items.Select(i => new OrderItem
                    {
                        Id = Guid.NewGuid(),
                        ProductId = i.ProductId,
                        Quantity = i.Quantity,
                        UnitPrice = i.UnitPrice
                    }).ToList()
                };

                await _orderRepository.CreateOrderAsync(order);
                _logger.LogInformation("Order created successfully with ID: {OrderId}", order.Id);

                // Create payment intent request
                var paymentIntentRequest = new CreatePaymentIntentRequest
                {
                    OrderId = order.Id,
                    Items = request.Items,
                    TotalAmount = totalAmount,
                    UserInfo = new UserInfo
                    {
                        UserId = userId,
                        IsGuest = userId == "guest"
                    }
                };

                // Call the PaymentClient
                var paymentIntentResponse = await _paymentClient.CreatePaymentIntentAsync(paymentIntentRequest);

                await transaction.CommitAsync();

                return Ok(new
                {
                    orderId = order.Id,
                    clientSecret = paymentIntentResponse.ClientSecret
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error starting checkout");
                return StatusCode(500, new { message = "Internal server error. Please try again later." });
            }
        }

        private string GenerateIdempotencyKey(string userId, StartCheckoutRequest request)
        {
            var keyBase = $"{userId}:{request.Items.Sum(i => i.ProductId.GetHashCode() + i.Quantity)}:{DateTime.UtcNow.Date}";
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(keyBase));
        }
    }
}
