using System;
using Microsoft.AspNetCore.Identity;

namespace haworks.Db
{
    public class UserProfile : AuditableEntity
    {
        public string UserId { get; set; } = string.Empty;

        public string Bio { get; set; } = string.Empty;

        public string AvatarUrl { get; set; } = string.Empty;

        // Social or website links
        public string Website { get; set; } = string.Empty;

        public User? User { get; set; }

        public DateTime? LastLogin { get; set; }
    }
}
