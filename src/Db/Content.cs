using System;

namespace haworks.Db
{
    public class Content : AuditableEntity
    {
        public Content(Guid id) : base(id) 
        {
        }
        public Content() : base()
        {
        }
        public Guid EntityId { get; set; } 
        public string EntityType { get; set; } = string.Empty; 
        public ContentType ContentType { get; set; } 
        public string Url { get; set; } = string.Empty; 
        public string BlobName { get; set; } = string.Empty; 

    }
}
