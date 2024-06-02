using RitualWorks.Db;

namespace RitualWorks.DTOs
{
    public class RitualDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string FullContent { get; set; } = string.Empty; // For custom uploaded content
        public string ExternalLink { get; set; } = string.Empty;// For external content like YouTube videos
        public decimal TokenAmount { get; set; }
        public RitualTypeEnum RitualType { get; set; } // Use the enum here
        public bool IsLocked { get; set; }
        public double Rating { get; set; }
    }
}
