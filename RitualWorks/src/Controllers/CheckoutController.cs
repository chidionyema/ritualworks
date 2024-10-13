using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RitualWorks.Contracts;
using RitualWorks.Db;

namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CheckoutController : ControllerBase
    {
        private readonly ILogger<CheckoutController> _logger;
        private readonly IOrderRepository _orderRepository;
        private readonly IProductRepository _productRepository;
        private readonly IHttpClientFactory _httpClientFactory;

        public CheckoutController(
            ILogger<CheckoutController> logger,
            IOrderRepository orderRepository,
            IProductRepository productRepository,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _orderRepository = orderRepository;
            _productRepository = productRepository;
            _httpClientFactory = httpClientFactory;
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

                // Prepare the request payload for creating a checkout session
                var sessionPayload = new
                {
                    payment_method_types = new[] { "card" },
                    line_items = items.Select(item => new
                    {
                        price_data = new
                        {
                            currency = "usd",
                            product_data = new
                            {
                                name = item.Name
                            },
                            unit_amount = (long)(item.Price * 100) // Stripe expects amounts in cents
                        },
                        quantity = item.Quantity
                    }).ToList(),
                    mode = "payment",
                    success_url = $"{domain}/success?session_id={{CHECKOUT_SESSION_ID}}",
                    cancel_url = $"{domain}/cancel"
                };

                var client = _httpClientFactory.CreateClient();

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.stripe.com/v1/checkout/sessions")
                {
                    Content = new StringContent(JsonSerializer.Serialize(sessionPayload), Encoding.UTF8, "application/json")
                };
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY"));

                // Log the request headers
                _logger.LogInformation("Request Headers:");
                foreach (var header in requestMessage.Headers)
                {
                    _logger.LogInformation($"Header: {header.Key} = {string.Join(", ", header.Value)}");
                }

                // Log the request content
                var content = await requestMessage.Content.ReadAsStringAsync();
                _logger.LogInformation($"Request Content: {content}");

                var response = await client.SendAsync(requestMessage);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Stripe API error occurred: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                    return StatusCode(500, "Payment processing error. Please try again later.");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var stripeSession = JsonSerializer.Deserialize<StripeCheckoutSession>(jsonResponse);

                // Create order in the system
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
                    Status = OrderStatus.Pending,
                };

                await _orderRepository.CreateOrderAsync(order);

                _logger.LogInformation($"Checkout session created successfully for User {userId} with Order ID {order.Id}.");
                return Ok(new { id = stripeSession?.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while creating the checkout session.");
                return StatusCode(500, "An internal server error occurred. Please try again later.");
            }
        }
    }

    // Helper DTO class for deserializing Stripe response
    public class StripeCheckoutSession
    {
        public string Id { get; set; } = string.Empty;
    }

    // DTO class for items in the checkout
    public class CheckoutItem
    {
        public Guid ProductId { get; set; }
        public long Quantity { get; set; }
        public decimal Price { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
