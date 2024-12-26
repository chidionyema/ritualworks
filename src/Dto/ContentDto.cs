using System;

namespace haworks.Dto
{
    public class ContentDto
    {
        public Guid Id { get; set; } // Unique identifier for the content
        public string Url { get; set; } = string.Empty; // URL of the content
        public string BlobName { get; set; } = string.Empty; // Blob or file name
        public string EntityType { get; set; } = string.Empty; // Type of the related entity (e.g., "Product")
        public Guid EntityId { get; set; } // ID of the related entity
        public haworks.Db.ContentType ContentType { get; set; } // Enum representing the type of content (e.g., Image, Asset)
        public DateTime CreatedAt { get; set; } // Timestamp when the content was created
    }
}
