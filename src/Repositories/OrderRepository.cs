﻿using haworks.Db;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using haworks.Contracts;
using Microsoft.EntityFrameworkCore.Storage;

namespace haworks.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly haworksContext _context;
        private readonly ILogger<OrderRepository> _logger;

        public OrderRepository(haworksContext context, ILogger<OrderRepository> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Order?> GetOrderByIdempotencyKeyAsync(string idempotencyKey)
        {
            return await _context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey);
        }

        public async Task<IEnumerable<Order>> GetOrdersAsync()
        {
            try
            {
                _logger.LogInformation("Fetching all orders with no tracking.");
                return await _context.Orders
                    .AsNoTracking()
                    .Include(o => o.OrderItems!) // Using the null-forgiving operator
                    .ThenInclude(oi => oi.Product)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching orders.");
                throw;
            }
        }

        // Option A: Change return type to Task<Order?> if null is acceptable.
        public async Task<Order?> GetOrderByIdAsync(Guid id)
        {
            try
            {
                _logger.LogInformation("Fetching order with ID: {OrderId} using no tracking.", id);
                var order = await _context.Orders
                    .AsNoTracking()
                    .Include(o => o.OrderItems!)
                        .ThenInclude(oi => oi.Product)
                    .FirstOrDefaultAsync(o => o.Id == id);

                if (order == null)
                {
                    _logger.LogWarning("Order with ID {OrderId} not found.", id);
                }

                return order;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching order with ID {OrderId}.", id);
                throw;
            }
        }

        public async Task CreateOrderAsync(Order order)
        {
            try
            {
                if (order == null)
                {
                    throw new ArgumentNullException(nameof(order), "Order cannot be null.");
                }

                _logger.LogInformation("Creating a new order.");
                await _context.Orders.AddAsync(order);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Order created successfully with ID: {OrderId}", order.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while creating a new order.");
                throw;
            }
        }

        public async Task UpdateOrderStatusAsync(Guid orderId, OrderStatus status)
        {
            try
            {
                _logger.LogInformation("Updating status for order ID: {OrderId} to {Status}.", orderId, status);
                var order = await _context.Orders.FindAsync(orderId);

                if (order == null)
                {
                    _logger.LogWarning("Order with ID {OrderId} not found. Status update skipped.", orderId);
                    return;
                }

                order.Status = status;
                await _context.SaveChangesAsync();
                _logger.LogInformation("Order status updated successfully for ID: {OrderId}.", orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating the status for order ID {OrderId}.", orderId);
                throw;
            }
        }

        public async Task CreateGuestUserAsync(string guestId)
        {
            // Check if a user with the given guestId already exists.
            var existingUser = await _context.Users.FindAsync(guestId);
            if (existingUser == null)
            {
                var guestUser = new User
                {
                    // Use the guestId as the primary key.
                    Id = guestId,
                    // Create a guest username and a dummy email.
                    UserName = $"guest_{guestId}",
                    Email = $"{guestId}@guest.example.com"
                };

                await _context.Users.AddAsync(guestUser);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created guest user with ID: {GuestId}", guestId);
            }
            else
            {
                _logger.LogInformation("Guest user with ID: {GuestId} already exists.", guestId);
            }
        }

        public async Task<IDbContextTransaction> BeginTransactionAsync()
        {
            return await _context.Database.BeginTransactionAsync();
        }

        public async Task SaveChangesAsync()
        {
            try
            {
                _logger.LogInformation("Saving changes to the database.");
                await _context.SaveChangesAsync();
                _logger.LogInformation("Database changes saved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while saving changes to the database.");
                throw;
            }
        }
    }
}
