using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using haworks.Db;
using Microsoft.EntityFrameworkCore.Storage;

namespace haworks.Contracts
{
    public interface IOrderRepository
    {
        Task<IEnumerable<Order>> GetOrdersAsync();
        Task<Order> GetOrderByIdAsync(Guid id);
        Task CreateOrderAsync(Order order);
        Task UpdateOrderStatusAsync(Guid orderId, OrderStatus status);
        Task SaveChangesAsync();
        Task<Order?> GetOrderByIdempotencyKeyAsync(string idempotencyKey);
        Task<IDbContextTransaction> BeginTransactionAsync();
    }
}

