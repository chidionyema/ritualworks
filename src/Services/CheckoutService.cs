using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RitualWorks.Contracts;
using RitualWorks.Controllers;
using RitualWorks.Db;

namespace RitualWorks.Services
{
    public class CheckoutService
    {
        private readonly IProductRepository _productRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly RitualWorksContext _context;
        private readonly IPaymentService _paymentService;
        private readonly ILogger<CheckoutService> _logger;

        public CheckoutService(
            IProductRepository productRepository,
            IOrderRepository orderRepository,
            RitualWorksContext context,
            IPaymentService paymentService,
            ILogger<CheckoutService> logger)
        {
            _productRepository = productRepository;
            _orderRepository = orderRepository;
            _context = context;
            _paymentService = paymentService;
            _logger = logger;
        }

        public async Task<string> CreateCheckoutSessionAsync(List<CheckoutItem> items, string userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    OrderDate = DateTime.UtcNow,
                    UserId = userId,
                    OrderItems = new List<OrderItem>(),
                    TotalAmount = 0
                };

                foreach (var item in items)
                {
                    var product = await _productRepository.GetProductByIdAsync(item.ProductId);
                    if (product == null)
                    {
                        throw new ArgumentException($"Product with ID {item.ProductId} not found.");
                    }

                    if (product.Stock < item.Quantity)
                    {
                        throw new ArgumentException($"Insufficient stock for product {product.Name}.");
                    }

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

                await _orderRepository.AddOrderAsync(order);

                var sessionId = await _paymentService.CreateCheckoutSessionAsync(userId, items);

                await transaction.CommitAsync();

                return sessionId;
            }
            catch (ArgumentException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "An error occurred while creating the checkout session.");
                throw;
            }
        }
    }
}
