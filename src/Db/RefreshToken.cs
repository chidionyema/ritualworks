using System;
using Microsoft.AspNetCore.Identity;

namespace haworks.Db
{
    public class RefreshToken : AuditableEntity
    {

        // Foreign key to the User
        public string UserId { get; set; } = string.Empty;
        
        // Navigation property to the Identity User
        public User User { get; set; } = null!;

        // The token string
        public string Token { get; set; } = string.Empty;
        
        // Expiration date for the token
        public DateTime Expires { get; set; }
    }


     public class RevokedToken : AuditableEntity
    {
        public string Jti { get; set; }
        public string UserId { get; set; }
        public DateTime ExpiryDate { get; set; }
    }
}
