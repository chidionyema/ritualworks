using System;
using System.Collections.Generic;

namespace RitualWorks.Db
{
    public class Petition
    {
        public int Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public int RitualId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public Ritual? Ritual { get; set; }
        public User? User { get; set; }
        public ICollection<Donation>? Donations { get; set; }
        public DateTime Created { get; set; } = DateTime.UtcNow; // Default to current UTC time
    }
}
