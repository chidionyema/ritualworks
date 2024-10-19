using RitualWorks.Db;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RitualWorks.Contracts;

namespace RitualWorks.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly RitualWorksContext _context;

        public OrderRepository(RitualWorksContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Order>> GetOrdersAsync()
        {
            return await _context.Orders.Include(o => o.OrderItems).ThenInclude(oi => oi.Product).ToListAsync();
        }

        public async Task<Order> GetOrderByIdAsync(Guid id)
        {
            return await _context.Orders.Include(o => o.OrderItems).ThenInclude(oi => oi.Product).FirstOrDefaultAsync(o => o.Id == id);
        }

        public async Task CreateOrderAsync(Order order)
        {
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateOrderStatusAsync(Guid orderId, OrderStatus status)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order != null)
            {
                order.Status = status;
                await _context.SaveChangesAsync();
            }
        }


        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}

