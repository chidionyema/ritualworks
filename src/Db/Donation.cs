using System;

namespace RitualWorks.Db
{
    public class Donation
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public int? PetitionId { get; set; } // Nullable for direct donations to a ritual
        public int? RitualId { get; set; } // Nullable for donations linked to a petition
        public string UserId { get; set; } = string.Empty;
        public Petition? Petition { get; set; } // Renamed to Petition for clarity
        public Ritual? Ritual { get; set; }
        public User? User { get; set; }
        public DateTime Created { get; set; } = DateTime.UtcNow; // Default to current UTC time
    }
}
