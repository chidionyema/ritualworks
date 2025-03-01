using System;
using System.ComponentModel.DataAnnotations;

namespace haworks.Dto
{
    public record SubscriptionRequest
    {
        [Required]
        public string PriceId { get; init; } = string.Empty;
        
        [Required]
        public string RedirectPath { get; init; } = string.Empty;
    }

    public record SubscriptionStatusResponseDto
    {
        public bool IsSubscribed { get; init; }
        public string? PlanId { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }
    }

    public record CreateCheckoutSessionResponseDto
    {
        public string SessionId { get; init; } = string.Empty;
    }
}
