using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Stripe.Checkout;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using RitualWorks.Contracts;
using RitualWorks.Db;
using System.Security.Claims;
using Stripe;

namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CheckoutController : ControllerBase
    {
        private readonly ILogger<CheckoutController> _logger;
        private readonly IOrderRepository _orderRepository;
        private readonly IProductRepository _productRepository;

        public CheckoutController(
            ILogger<CheckoutController> logger, 
            IOrderRepository orderRepository, 
            IProductRepository productRepository)
        {
            _logger = logger;
            _orderRepository = orderRepository;
            _productRepository = productRepository;
        }

        [HttpPost("create-session")]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] List<CheckoutItem> items)
        {
            if (items == null || !items.Any())
            {
                _logger.LogWarning("CreateCheckoutSession called with no items.");
                return BadRequest("No items in the checkout.");
            }

            try
            {
                var productIds = items.Select(i => i.ProductId).ToList();
                var products = await _productRepository.GetProductsByIdsAsync(productIds);

                if (products.Count != items.Count)
                {
                    _logger.LogWarning("Some products in the checkout are invalid.");
                    return BadRequest("Some products are invalid.");
                }

                foreach (var item in items)
                {
                    var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                    if (product == null || product.Price != item.Price)
                    {
                        _logger.LogWarning($"Invalid price for product {item.Name}.");
                        return BadRequest($"Invalid price for product {item.Name}.");
                    }

                    // Check for sufficient stock
                    if (product.Stock < item.Quantity)
                    {
                        _logger.LogWarning($"Insufficient stock for product {item.Name}. Requested: {item.Quantity}, Available: {product.Stock}");
                        return BadRequest($"Insufficient stock for product {item.Name}.");
                    }
                }

                // Derive the domain dynamically from the incoming request
                var domain = $"{Request.Scheme}://{Request.Host}";

                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = items.Select(item => new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)(item.Price * 100),
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = item.Name
                            }
                        },
                        Quantity = (int)item.Quantity
                    }).ToList(),
                    Mode = "payment",
                    SuccessUrl = $"{domain}/success?session_id={{CHECKOUT_SESSION_ID}}",
                    CancelUrl = $"{domain}/cancel"
                };

                var service = new SessionService(new Stripe.StripeClient("your_stripe_secret_key")); // Ensure correct Stripe API key
                var session = await service.CreateAsync(options);

                // Create order
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("User ID could not be found in the claims.");
                    return Unauthorized("User authentication failed.");
                }

                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    OrderDate = DateTime.UtcNow,
                    UserId = userId,
                    OrderItems = items.Select(i => new OrderItem
                    {
                        ProductId = i.ProductId,
                        Quantity = (int)i.Quantity
                    }).ToList(),
                    TotalAmount = items.Sum(i => i.Price * i.Quantity),
                    Status = OrderStatus.Pending
                };

                await _orderRepository.CreateOrderAsync(order);

                _logger.LogInformation($"Checkout session created successfully for User {userId} with Order ID {order.Id}.");
                return Ok(new { id = session.Id });
            }
            catch (StripeException stripeEx)
            {
                _logger.LogError(stripeEx, "Stripe API error occurred during checkout session creation.");
                return StatusCode(500, "Payment processing error. Please try again later.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while creating the checkout session.");
                return StatusCode(500, "An internal server error occurred. Please try again later.");
            }
        }
    }

    public class CheckoutItem
    {
        public Guid ProductId { get; set; }
        public long Quantity { get; set; }
        public decimal Price { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
