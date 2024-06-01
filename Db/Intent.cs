namespace RitualWorks.Db
{
    public class Intent
    {
        public int Id { get; set; }
        public string? Description { get; set; }
        public int RitualId { get; set; }
        public Ritual? Ritual { get; set; }
        public string? UserId { get; set; }
        public User? User { get; set; }
        public DateTime? Created { get; set; }
        public DateTime? Updated { get; set; }
    }

}