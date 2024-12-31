
using System;
using System.Collections.Generic;

namespace haworks.Db
{
    public class Payment
    {
        public Guid Id { get; set; }
        public DateTime OrderDate { get; set; }
        public string UserId { get; set; } = string.Empty;
        public Guid OrderId { get; set; }
        public Order Order { get; set; }
        public string? StripeSessionId { get; internal set; }
         public string? PaymentIntentId { get; internal set; }
        public decimal Amount { get; set; }
        public decimal Tax { get; set; }
        public bool IsComplete { get; set; } 
        public PaymentStatus Status { get; set; } // Added status field
    }
}

