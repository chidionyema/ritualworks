 
 using System;
 namespace haworks.Dto
{ 
    public class CheckoutItem
    {
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; } 
        public string Name { get; set; }
    }
}
