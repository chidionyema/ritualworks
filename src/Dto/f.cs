using System;

namespace haworks.Dto
{
    public class StartCheckoutRequest
    {
        public CheckoutItem[] Items { get; set; } = Array.Empty<CheckoutItem>();
        public GuestInfo? GuestInfo { get; set; }
    }
}
