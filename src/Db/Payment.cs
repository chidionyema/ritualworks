using System;

namespace haworks.Db
{
    public class Payment : AuditableEntity
    {
        // The Id property is inherited from AuditableEntity

        // Date of the order/payment
        public DateTime OrderDate { get; set; }

        // Foreign key to the user (as string, matching Identity)
        public string UserId { get; set; } = string.Empty;

        // Foreign key to the associated order
        public Guid OrderId { get; set; }

        // Navigation property to the order
        public Order Order { get; set; } = null!;

        // Stripe and PaymentIntent identifiers (internal setters)
        public string? StripeSessionId { get; internal set; }

        public string TransactionId { get; set; } = string.Empty;
        public string? PaymentIntentId { get; internal set; }

        // Payment amount and tax
        public decimal Amount { get; set; }
        public decimal Tax { get; set; }

        // Flag indicating whether the payment is complete
        public bool IsComplete { get; set; }

        // Payment status enumeration (ensure PaymentStatus enum is defined)
        public PaymentStatus Status { get; set; }
    }
}
