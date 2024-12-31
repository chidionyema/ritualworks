 
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
}




