using System;
using System.Collections.Generic;

namespace haworks.Db
{
    public class Product : AuditableEntity
    {
        public Product(Guid id, string name, string description, decimal price, Guid categoryId) : base(id)
        {
            Name = name;
            Description = description;
            UnitPrice = price;
            CategoryId = categoryId;
        }

        public Product(string name, string description, decimal price, Guid categoryId) : base()
        {
            Name = name;
            Description = description;
            UnitPrice = price;
            CategoryId = categoryId;
        }

        public Product() : base() { }

        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public double Rating { get; set; }
        public bool IsListed { get; set; }
        public bool IsFeatured { get; set; }
        public int Stock { get; set; }
        public bool IsInStock { get; set; }
        public string? Brand { get; set; } = string.Empty;
        public string? Type { get; set; } = string.Empty;
        public Guid CategoryId { get; set; }
        public Category? Category { get; set; }
        public List<Content>? Contents { get; set; } // Navigation property for related content
        public List<ProductReview>? ProductReviews { get; set; }

        
    }
}
