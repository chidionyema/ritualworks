using System;

namespace haworks.Db
{
    public class Content : AuditableEntity
    {
        public Content() : base() // Explicitly calling the parameterless base constructor
        {
            Id = Guid.NewGuid(); // Ensure a valid GUID is always assigned
        }
        public Guid Id { get; set; }
        public Guid EntityId { get; set; } // Generic reference to any entity (e.g., Product, Category)
        public string EntityType { get; set; } = string.Empty; // E.g., "Product", "Category"
        public ContentType ContentType { get; set; } // Enum to represent content type
        public string Url { get; set; } = string.Empty; // File or blob URL
        public string BlobName { get; set; } = string.Empty; // File storage reference
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
