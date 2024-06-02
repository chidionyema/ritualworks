namespace RitualWorks.DTOs
{
    public class DonationDto
    {
        public int Id { get; set; }
        public int? PetitionId { get; set; }
        public int? RitualId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string DonorName { get; set; } = string.Empty;
    }

}