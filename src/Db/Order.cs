
using System;
using System.Collections.Generic;

namespace RitualWorks.Db
{

    public class Order
    {
        public Guid Id { get; set; }
        public DateTime OrderDate { get; set; }
        public string UserId { get; set; } = string.Empty;
        public ICollection<OrderItem>? OrderItems { get; set; }
        public decimal TotalAmount { get; set; }
        public OrderStatus Status { get; set; } // Added status field
    }
}

