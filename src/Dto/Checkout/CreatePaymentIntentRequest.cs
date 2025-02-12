 
 using System;
 using System.ComponentModel.DataAnnotations; 

 namespace haworks.Dto
{ 
    public class CreatePaymentIntentRequest
    {  
            [Required]
            public Guid OrderId { get; set; }
            [Required]
            public CheckoutItem[] Items { get; set; } = Array.Empty<CheckoutItem>();
            [Required]
            public decimal TotalAmount { get; set; } 
            [Required]
            public UserInfo UserInfo { get; set; } = new UserInfo();
    } 
       public class CreatePaymentIntentResponse
       {
            public string SessionId { get; set; }
       }
       public class ConfirmPaymentRequest
       {
            public string SessionId { get; set; } // The Stripe Checkout Session ID
            public Guid OrderId { get; set; }    // The Order ID generated during checkout
       }

}




