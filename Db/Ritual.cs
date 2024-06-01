namespace RitualWorks.Db
{
    public class Ritual
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? TextContent { get; set; }
        public string? AudioUrl { get; set; }
        public string? VideoUrl { get; set; }
        public string? CreatorId { get; set; }
        public User? Creator { get; set; }
        public ICollection<Intent>? Intents { get; set; }
        public ICollection<Donation>? Donations { get; set; }
        public RitualType? RitualType { get; set; }
        public int RitualTypeId { get; set; } // Foreign key
        public DateTime? Created { get; set; }
        public DateTime? Updated { get; set; }
    }

}