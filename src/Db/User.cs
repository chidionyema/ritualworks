using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
namespace RitualWorks.Db
{
     public class User : IdentityUser
    {
        public ICollection<Ritual>? Rituals { get; set; }
        public ICollection<Post>? Posts { get; set; }
        public ICollection<Comment>? Comments { get; set; }
    }
}