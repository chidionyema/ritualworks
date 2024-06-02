
namespace RitualWorks.DTOs
{
    public class CreatePetitionDto
    {
        public int RitualId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}

