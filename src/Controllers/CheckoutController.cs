using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using haworks.Contracts;
using haworks.Db;
using haworks.Dto;
using System.IO;
using haworks.Repositories;
using Stripe;
using Stripe.Checkout;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace haworks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CheckoutController : ControllerBase
    {
        private readonly ILogger<CheckoutController> _logger;
        private readonly IOrderRepository _orderRepository;
        private readonly IProductRepository _productRepository;
        private readonly IPaymentRepository _paymentRepository;
        private readonly IConfiguration _configuration;

        public CheckoutController(
            ILogger<CheckoutController> logger,
            IOrderRepository orderRepository,
            IProductRepository productRepository,
            IPaymentRepository paymentRepository,
            IConfiguration configuration)
        {
            _logger = logger;
            _orderRepository = orderRepository;
            _productRepository = productRepository;
            _paymentRepository = paymentRepository;
            _configuration = configuration;
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartCheckout([FromBody] StartCheckoutRequest request)
        {
            _logger.LogInformation("StartCheckout request received: {@Request}", request);

            if (request == null || request.Items?.Any() != true)
            {
                _logger.LogWarning("StartCheckout failed: No items provided.");
                return BadRequest(new { message = "No items provided." });
            }

            try
            {
                // Step 1: User and Idempotency Key
                var userId = User.Identity?.IsAuthenticated == true
                    ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown"
                    : "guest";

                var idempotencyKey = GenerateIdempotencyKey(userId, request);

                // Step 2: Check for Existing Order
                var existingOrder = await _orderRepository.GetOrderByIdempotencyKeyAsync(idempotencyKey);
                if (existingOrder != null)
                {
                    _logger.LogInformation("Order already processed for IdempotencyKey: {IdempotencyKey}", idempotencyKey);
                    return Ok(new { orderId = existingOrder.Id });
                }

                // Step 3: Validate Products
                foreach (var item in request.Items)
                {
                    _logger.LogDebug("Checking product with ID: {ProductId}", item.ProductId);

                    var product = await _productRepository.GetProductByIdAsync(item.ProductId);
                    if (product == null)
                    {
                        _logger.LogWarning("StartCheckout failed: Product with ID {ProductId} not found.", item.ProductId);
                        return BadRequest(new { message = $"Product with ID {item.ProductId} does not exist." });
                    }

                    if (product.Stock < item.Quantity)
                    {
                        _logger.LogWarning("StartCheckout failed: Product {ProductName} (ID: {ProductId}) is out of stock.", product.Name, product.Id);
                        return BadRequest(new { message = $"Product {product.Name} is out of stock." });
                    }

                    if (product.UnitPrice != item.UnitPrice)
                    {
                        _logger.LogWarning("Price mismatch for product {ProductName} (ID: {ProductId}).", product.Name, product.Id);
                        return BadRequest(new { message = $"Price of {product.Name} has changed. Please update your cart." });
                    }
                }

                // Step 4: Start Transaction and Create Order/Payment
                await using var transaction = await _orderRepository.BeginTransactionAsync();

                var totalAmount = request.Items.Sum(i => i.UnitPrice * i.Quantity);

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

                _logger.LogDebug("Creating order: {@Order}", order);
                await _orderRepository.CreateOrderAsync(order);

                var paymentRecord = new Payment
                {
                    Id = Guid.NewGuid(),
                    OrderDate = DateTime.UtcNow,
                    UserId = userId,
                    OrderId = order.Id,
                    Amount = totalAmount,
                    Tax = totalAmount * 0.1m, // Assuming a 10% tax rate
                    IsComplete = false,
                    Status = PaymentStatus.Pending,
                    StripeSessionId = null // Initialize as null, update after Stripe session creation
                };

                _logger.LogInformation("Creating payment record: {@PaymentRecord}", paymentRecord);
                await _paymentRepository.CreatePaymentAsync(paymentRecord);

                // Step 5: Create Stripe Checkout Session
                var stripeSecretKey = _configuration["Stripe:SecretKey"];
                if (string.IsNullOrEmpty(stripeSecretKey))
                {
                    _logger.LogError("Stripe SecretKey is not configured.");
                    throw new InvalidOperationException("Stripe SecretKey is not configured.");
                }

                StripeConfiguration.ApiKey = stripeSecretKey;

                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = request.Items.Select(item => new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = item.Name,
                            },
                            UnitAmount = (long)(item.UnitPrice * 100), // Amount in cents
                        },
                        Quantity = item.Quantity,
                    }).ToList(),
                    Mode = "payment",
                    SuccessUrl = $"https://yourdomain.com/checkout/success?orderId={order.Id}",
                    CancelUrl = "https://yourdomain.com/cancel",
                };

                _logger.LogInformation("Creating Stripe Checkout Session with options: {@Options}", options);
                var sessionService = new SessionService();
                var session = await sessionService.CreateAsync(options);
                _logger.LogInformation("Stripe Checkout Session created: {SessionId}", session.Id);

                // Update payment record with Stripe session ID
                paymentRecord.StripeSessionId = session.Id;
                await _paymentRepository.UpdatePaymentAsync(paymentRecord);

                await transaction.CommitAsync();
                _logger.LogInformation("Transaction committed for OrderId: {OrderId}", order.Id);

                return Ok(new
                {
                    orderId = order.Id,
                    sessionId = session.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting checkout for request: {@Request}", request);
                return StatusCode(500, new { message = "Internal server error. Please try again later." });
            }
        }

        /// <summary>
        /// Webhook endpoint to handle Stripe events
        /// </summary>
        [HttpPost("webhook")]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            string endpointSecret = _configuration["Stripe:WebhookSecret"];
            Event stripeEvent;

            try
            {
                var signatureHeader = Request.Headers["Stripe-Signature"];
                stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, endpointSecret);
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe webhook signature verification failed.");
                return BadRequest();
            }

            // Handle the event
            if (stripeEvent.Type == Stripe.Events.CheckoutSessionCompleted)
            {
                var session = stripeEvent.Data.Object as Session;
                await HandleCheckoutSessionCompleted(session);
            }
            else
            {
                _logger.LogInformation($"Unhandled event type: {stripeEvent.Type}");
            }

            return Ok();
        }

        private async Task HandleCheckoutSessionCompleted(Session session)
        {
            _logger.LogInformation($"Handling CheckoutSessionCompleted for session: {session.Id}");

            // Retrieve the Payment record using StripeSessionId
            var paymentRecord = await _paymentRepository.GetPaymentByStripeSessionIdAsync(session.Id);
            if (paymentRecord == null)
            {
                _logger.LogError($"Payment record not found for Stripe Session ID: {session.Id}");
                return;
            }

            // Update the payment record
            paymentRecord.Status = PaymentStatus.Completed;
            paymentRecord.IsComplete = true;
            await _paymentRepository.UpdatePaymentAsync(paymentRecord);

            // Update the order status
            var order = await _orderRepository.GetOrderByIdAsync(paymentRecord.OrderId);
            if (order == null)
            {
                _logger.LogError($"Order not found with ID: {paymentRecord.OrderId}");
                return;
            }

            order.Status = OrderStatus.Completed;
            await _orderRepository.UpdateOrderStatusAsync(order.Id, order.Status);

            _logger.LogInformation($"Order {order.Id} marked as Completed.");
        }

        private string GenerateIdempotencyKey(string userId, StartCheckoutRequest request)
        {
            var keyBase = $"{userId}:{request.Items.Sum(i => i.ProductId.GetHashCode() + i.Quantity)}:{DateTime.UtcNow.Date}";
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(keyBase));
        }
    }
}