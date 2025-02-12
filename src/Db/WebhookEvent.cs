
using System;
namespace haworks.Db {
    public class WebhookEvent
    {
        public Guid Id { get; set; }
        public string StripeEventId { get; set; } // Unique ID from Stripe
        public string EventType { get; set; } // e.g., "checkout.session.completed"
        public string EventJson { get; set; } = string.Empty;
        public string RawJson { get; set; } // Raw payload
        public DateTime ProcessedAt { get; set; }
        public bool IsProcessed { get; set; }
        public string Error { get; set; } // Store processing errors
    }
}