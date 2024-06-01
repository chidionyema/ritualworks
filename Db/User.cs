namespace RitualWorks.Db
{
    using Microsoft.AspNetCore.Identity;
    using System.Collections.Generic;

    public class User : IdentityUser
    {
        public ICollection<Ritual>? Rituals { get; set; }
        public ICollection<Intent>? Intents { get; set; }
        public ICollection<Donation>? Donations { get; set; }
    }

}