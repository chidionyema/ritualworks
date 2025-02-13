using System;
namespace haworks.Db
{
    public class Subscription
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string StripeSubscriptionId { get; set; } = string.Empty;
        public string PlanId { get; set; } = string.Empty;
        public SubscriptionStatus Status { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class SubscriptionPlan
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string StripePriceId { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string? Description { get; set; }
    }

    public enum SubscriptionStatus
    {
        Active,
        Canceled,
        Incomplete,
        Unknown,
        PastDue,
        Trialing,
        Expired,
        Unpaid
    }
}