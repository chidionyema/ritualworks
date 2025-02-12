 using System;
    public class Subscription
    {
        public Guid Id { get; set; }
        public string UserId { get; set; }
        public string StripeSubscriptionId { get; set; }
        public string PlanId { get; set; }
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

            // Optional: A brief description of the plan.
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