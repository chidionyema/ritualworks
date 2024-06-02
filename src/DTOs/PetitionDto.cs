using System;

namespace RitualWorks.DTOs
{
    public class PetitionDto
    {
        public int Id { get; set; }
        public int RitualId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Created { get;  set; }
    }
}

