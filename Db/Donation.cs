namespace RitualWorks.Db
{
    public class Donation
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public int RitualId { get; set; }
        public Ritual? Ritual { get; set; }
        public string? UserId { get; set; }
        public User? User { get; set; }
        public DateTime? Created { get; set; }
        public DateTime? Updated { get; set; }
    }
}