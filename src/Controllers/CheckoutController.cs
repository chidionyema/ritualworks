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

namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CheckoutController : ControllerBase
    {
        private readonly ILogger<CheckoutController> _logger;
        private readonly IOrderRepository _orderRepository;
        private readonly IProductRepository _productRepository;

        public CheckoutController(ILogger<CheckoutController> logger, IOrderRepository orderRepository, IProductRepository productRepository)
        {
            _logger = logger;
            _orderRepository = orderRepository;
            _productRepository = productRepository;
        }

        [HttpPost("create-session")]
        // [Authorize]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] List<CheckoutItem> items)
        {
            if (items == null || !items.Any())
            {
                return BadRequest("No items in the checkout.");
            }

            var productIds = items.Select(i => i.ProductId).ToList();
            var products = await _productRepository.GetProductsByIdsAsync(productIds);

            if (products.Count != items.Count)
            {
                return BadRequest("Some products are invalid.");
            }

            foreach (var item in items)
            {
                var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (product == null || product.Price != item.Price)
                {
                    return BadRequest($"Invalid price for product {item.Name}.");
                }
            }

            var domain = "http://localhost:3000"; // Update to your frontend URL
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

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            // Create order
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderDate = DateTime.UtcNow,
                UserId = "1",
                OrderItems = items.Select(i => new OrderItem
                {
                    ProductId = i.ProductId,
                    Quantity = (int)i.Quantity
                }).ToList(),
                TotalAmount = items.Sum(i => i.Price * i.Quantity),
                Status = OrderStatus.Pending
            };

            await _orderRepository.CreateOrderAsync(order);

            return Ok(new { id = session.Id });
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
