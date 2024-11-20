using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
namespace haworks.Db
{
     public class User : IdentityUser
    {
        public ICollection<Post>? Posts { get; set; }
        public ICollection<Comment>? Comments { get; set; }
    }
}