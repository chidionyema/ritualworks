using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
using System;
namespace haworks.Db
{
    public class UserProfile {
        
        public Guid Id { get; set; }
        // This is the same type as your Identity User primary key (string or Guid)
        public string UserId { get; set; } = null!;

        // A short bio or about text
        public string Bio { get; set; } = string.Empty;

        // Link to an avatar image or S3 bucket
        public string AvatarUrl { get; set; } = string.Empty;

        // Social or website links
        public string Website { get; set; } = string.Empty;

        // Navigation back to the Identity User
        public User? User { get; set; }
   }

}