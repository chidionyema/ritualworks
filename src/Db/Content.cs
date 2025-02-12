using System;

namespace haworks.Db
{
    public class Content : AuditableEntity
    {
        public Content(Guid id) : base(id) // Explicitly calling the parameterless base constructor
        {
        }
        public Content() : base()
        {
        }
        public Guid EntityId { get; set; } // Generic reference to any entity (e.g., Product, Category)
        public string EntityType { get; set; } = string.Empty; // E.g., "Product", "Category"
        public ContentType ContentType { get; set; } // Enum to represent content type
        public string Url { get; set; } = string.Empty; // File or blob URL
        public string BlobName { get; set; } = string.Empty; // File storage reference
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
