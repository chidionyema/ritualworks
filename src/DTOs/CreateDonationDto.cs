namespace RitualWorks.DTOs
{
    public class CreateDonationDto
    {
        public decimal Amount { get; set; }
        public int? PetitionId { get; set; } // Nullable for direct donations to a ritual
        public int? RitualId { get; set; } // Nullable for donations linked to a petition
        public string UserId { get; internal set; }
    }
}
