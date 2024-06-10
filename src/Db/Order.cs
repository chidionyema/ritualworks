
using System;
using System.Collections.Generic;

namespace RitualWorks.Db
{
    public enum OrderStatus
    {
        Pending,
        Completed,
        Failed,
        Cancelled
    }

    public class Order
    {
        public Guid Id { get; set; }
        public DateTime OrderDate { get; set; }
        public string UserId { get; set; } = string.Empty;
        public ICollection<OrderItem>? OrderItems { get; set; }
        public decimal TotalAmount { get; set; }
        public OrderStatus Status { get; set; } // Added status field
    }

    public class OrderItem
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Order? Order { get; set; } 
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }
}

