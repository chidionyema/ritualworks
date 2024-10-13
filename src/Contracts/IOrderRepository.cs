using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.Db;

namespace RitualWorks.Contracts
{
    public interface IOrderRepository
    {
        Task<IEnumerable<Order>> GetOrdersAsync();
        Task<Order> GetOrderByIdAsync(Guid id);
        Task CreateOrderAsync(Order order);
        Task UpdateOrderStatusAsync(Guid orderId, OrderStatus status);
    }
}

