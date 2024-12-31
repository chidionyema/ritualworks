using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using haworks.Db; 
using haworks.Events; 
using System;
using System.Linq;
using System.Threading.Tasks;

namespace haworks.Consumers
{
    public class CheckoutCompletedConsumer : IConsumer<CheckoutCompletedEvent>
    {
        private readonly haworksContext _dbContext;
        private readonly ILogger<CheckoutCompletedConsumer> _logger;
        private readonly IPublishEndpoint _publishEndpoint; 

        public CheckoutCompletedConsumer(
            haworksContext dbContext,
            ILogger<CheckoutCompletedConsumer> logger,
            IPublishEndpoint publishEndpoint)
        {
            _dbContext = dbContext;
            _logger = logger;
            _publishEndpoint = publishEndpoint;
        }

        public async Task Consume(ConsumeContext<CheckoutCompletedEvent> context)
        {
            var msg = context.Message;
            try
            {
                _logger.LogInformation($"Received CheckoutCompletedEvent. PaymentIntentId: {msg.PaymentIntentId}");

                // Calculate total
                decimal totalAmount = msg.Items.Sum(i => i.UnitPrice * i.Quantity);

                // Create Order using the constructor
                var order = new Order(
                    id: Guid.NewGuid(),
                  //  paymentIntentId: msg.PaymentIntentId,
                    totalAmount: totalAmount,
                    status: OrderStatus.Pending, // Use the enum value
                    userId: null // For guest checkouts
                );

                // Add order items
                foreach (var item in msg.Items)
                {
                    order.OrderItems.Add(new OrderItem
                    {
                        Id = Guid.NewGuid(),
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice
                    });
                }

                _dbContext.Orders.Add(order);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation($"Order {order.Id} created with total {totalAmount}.");

                // Publish an OrderCreatedEvent
                await _publishEndpoint.Publish<OrderCreatedEvent>(new
                {
                    OrderId = order.Id,
                    TotalAmount = order.TotalAmount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process CheckoutCompletedEvent");
                // Optionally handle compensation (e.g., publish a CheckoutFailedEvent)
            }
        }
    }

}
