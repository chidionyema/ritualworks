
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
        public decimal Amount { get; set; }
        public decimal Tax { get; set; }
        public PaymentStatus Status { get; set; } // Added status field
    }
}

