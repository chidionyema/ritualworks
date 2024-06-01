using System;
namespace RitualWorks.DTOs
{
    public class CreateRitualDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? TextContent { get; set; }
        public string? AudioUrl { get; set; }
        public string? VideoUrl { get; set; }
        public int RitualTypeId { get; set; }
    }

}

