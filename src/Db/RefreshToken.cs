using System;
using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
namespace haworks.Db
{
     public class RefreshToken
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } 
        public string Token { get; set; } = string.Empty;
        public DateTime Expires { get; set; }
        public DateTime Created { get; set; }
    }
}