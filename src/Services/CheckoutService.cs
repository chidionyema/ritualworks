using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RitualWorks.Contracts;
using RitualWorks.Controllers;
using RitualWorks.Db;
using RitualWorks.Repositories;
using Stripe.Checkout;

namespace RitualWorks.Services
{
    public class CheckoutService : ICheckoutService
    {
        private readonly IProductRepository _productRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly RitualWorksContext _context;

        public CheckoutService(IProductRepository productRepository, IOrderRepository orderRepository, RitualWorksContext context)
        {
            _productRepository = productRepository;
            _orderRepository = orderRepository;
            _context = context;
        }

        public async Task<(Order order, string sessionId)> CreateCheckoutSessionAsync(List<CheckoutItem> items, string userId, string domain)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var order = await CreateOrderAsync(items, userId);
                var sessionId = await CreatePaymentSessionAsync(order, items, domain);

                await transaction.CommitAsync();

                return (order, sessionId);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task UpdateOrderStatusAsync(Guid orderId, OrderStatus status)
        {
            await _orderRepository.UpdateOrderStatusAsync(orderId, status);
        }

        private async Task<Order> CreateOrderAsync(List<CheckoutItem> items, string userId)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderDate = DateTime.UtcNow,
                UserId = userId,
                OrderItems = new List<OrderItem>(),
                TotalAmount = 0,
                Status = OrderStatus.Pending // Set initial status to Pending
            };

            foreach (var item in items)
            {
                var product = await _productRepository.GetProductByIdAsync(item.ProductId);
                if (product == null)
                {
                    throw new Exception($"Product with ID {item.ProductId} not found.");
                }

                if (product.Stock < item.Quantity)
                {
                    throw new Exception($"Insufficient stock for product {product.Name}.");
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

                // Reduce the product stock
                product.Stock -= item.Quantity;
                await _productRepository.UpdateProductAsync(product);
            }

            await _orderRepository.AddOrderAsync(order);
            return order;
        }

        private async Task<string> CreatePaymentSessionAsync(Order order, List<CheckoutItem> items, string domain)
        {
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
                            Name = item.Name,
                        },
                    },
                    Quantity = item.Quantity,
                }).ToList(),
                Mode = "payment",
                SuccessUrl = $"{domain}/checkout/success?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{domain}/checkout/cancel",
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return session.Id;
        }
    }
}
