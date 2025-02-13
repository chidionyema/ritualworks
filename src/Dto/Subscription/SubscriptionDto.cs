using System;
using System.ComponentModel.DataAnnotations;

namespace haworks.Dto
{
    public class SubscriptionRequest
    {
        [Required]
        public string PriceId { get; set; } = string.Empty;
        
        [Required]
        public string RedirectPath { get; set; } = string.Empty;
    }

    public class SubscriptionStatusResponseDto
    {
        public bool IsSubscribed { get; set; }
        public string? PlanId { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    public class CreateCheckoutSessionResponseDto
    {
        public string SessionId { get; set; } = string.Empty;
    }
}
