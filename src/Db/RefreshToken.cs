using System;
using Microsoft.AspNetCore.Identity;

namespace haworks.Db
{
    public class RefreshToken
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!; // Using null-forgiving operator to indicate it will be set later
        public string Token { get; set; } = string.Empty;
        public DateTime Expires { get; set; }
        public DateTime Created { get; set; }
    }
}
