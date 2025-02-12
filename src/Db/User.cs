using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
namespace haworks.Db
{
     public class User : IdentityUser
    {
        public string CheckoutSessionId { get; set; }
        public string StripeCustomerId { get; set; }
    }
}