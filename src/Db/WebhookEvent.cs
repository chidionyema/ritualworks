using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace haworks.Db
{
    public enum HandlerType
    {
        Payment,
        Subscription,
        Unknown
    }

    public class WebhookEvent
    {
        public Guid Id { get; set; }
        
        // Enforce uniqueness via Fluent API in your DbContext.
        public string StripeEventId { get; set; } = string.Empty;
        
        public string EventType { get; set; } = string.Empty;
        public string EventJson { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public bool IsProcessed { get; set; }
        
        // Using an enum for HandlerType ensures only allowed values are used.
        public HandlerType HandlerType { get; set; } = HandlerType.Unknown;
        
        public string Error { get; set; } = string.Empty;

        // Optimistic concurrency token.
        [Timestamp]
        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>(); // Initialized to a non-null value
    }
}
