using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Stripe.Checkout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RitualWorks.Repositories;
using RitualWorks.Contracts;
using RitualWorks.Db;

namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CheckoutController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IProductRepository _productRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly RitualWorksContext _context;

        public CheckoutController(IConfiguration configuration, IProductRepository productRepository, IOrderRepository orderRepository, RitualWorksContext context)
        {
            _configuration = configuration;
            _productRepository = productRepository;
            _orderRepository = orderRepository;
            _context = context;
        }

        [HttpPost("create-session")]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] List<CheckoutItem> items)
        {
            if (items == null || !items.Any())
            {
                return BadRequest("No items in the checkout.");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var domain = _configuration["Domain"];
                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions>(),
                    Mode = "payment",
                    SuccessUrl = $"{domain}/checkout/success?session_id={{CHECKOUT_SESSION_ID}}",
                    CancelUrl = $"{domain}/checkout/cancel",
                };

                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    OrderDate = DateTime.UtcNow,
                    UserId = "user1", // Placeholder: Replace with actual user ID
                    OrderItems = new List<OrderItem>(),
                    TotalAmount = 0
                };

                foreach (var item in items)
                {
                    var product = await _productRepository.GetProductByIdAsync(item.ProductId);
                    if (product == null)
                    {
                        return BadRequest($"Product with ID {item.ProductId} not found.");
                    }

                    if (product.Stock < item.Quantity)
                    {
                        return BadRequest($"Insufficient stock for product {product.Name}.");
                    }

                    var sessionLineItem = new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)(product.Price * 100),
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = product.Name,
                            },
                        },
                        Quantity = item.Quantity,
                    };
                    options.LineItems.Add(sessionLineItem);

                    var orderItem = new OrderItem
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        Quantity = (int)item.Quantity,
                        Price = product.Price * item.Quantity,
                        OrderId = order.Id
                    };
                    order.OrderItems.Add(orderItem);
                    order.TotalAmount += orderItem.Price;
                }

                // Temporarily save the order to the database
                await _orderRepository.AddOrderAsync(order);

                var service = new SessionService();
                Session session = await service.CreateAsync(options);

                // Commit the transaction if Stripe session creation is successful
                await transaction.CommitAsync();

                return Ok(new { id = session.Id });
            }
            catch (Exception ex)
            {
                // Rollback the transaction if any error occurs
                await transaction.RollbackAsync();

                // Log the error (you can use any logging framework)
                Console.Error.WriteLine($"An error occurred: {ex.Message}");

                return StatusCode(500, "An error occurred while processing your request.");
            }
        }
    }

    public class CheckoutItem
    {
        public Guid ProductId { get; set; }
        public long Quantity { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
    }
}
