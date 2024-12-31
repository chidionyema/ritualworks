using System;
using System.ComponentModel.DataAnnotations;

namespace haworks.Db
{
    public interface IEntityWithGuid
    {
        Guid Id { get; }
    }

    public abstract class AuditableEntity : IEntityWithGuid
    {
        protected AuditableEntity()
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTime.UtcNow;
        }

        protected AuditableEntity(Guid id)
        {
            Id = id;
            CreatedAt = DateTime.UtcNow;
        }
        public Guid Id { get; protected set; }
   
       [ConcurrencyCheck]
        public int RowVersion { get; set; }     
        public string? CreatedBy { get; set; } 
        public DateTime CreatedAt { get; set; }
        public string? LastModifiedBy { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
}