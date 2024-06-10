using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.Controllers;
using RitualWorks.Db;

namespace RitualWorks.Services
{
    public interface ICheckoutService
    {
        public interface ICheckoutService
        {
            Task<(Order order, string sessionId)> CreateCheckoutSessionAsync(List<CheckoutItem> items, string userId, string domain);
            Task UpdateOrderStatusAsync(Guid orderId, OrderStatus status);
        }
    }
}
