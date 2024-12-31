using System;
using haworks.Controllers;
using haworks.Dto;
namespace haworks.Events
{
    public interface OrderCreatedEvent {
        Guid OrderId { get; }
        decimal TotalAmount { get; }
        // etc.
    }

    public interface CheckoutCompletedEvent
    {
        Guid CheckoutId { get; }
        string PaymentIntentId { get; }
        CheckoutItem[] Items { get; }
        GuestInfo GuestInfo { get; }
    }
}
