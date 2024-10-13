using System;
using System.Collections.Generic;

namespace RitualWorks.Db
{
    public class Product : AuditableEntity
    {
        // Constructor for creating a product with all main properties
        public Product(Guid id, string name, string description, decimal price, Guid categoryId) : base(id) // Call the base constructor with ID
        {
            Name = name;
            Description = description;
            Price = price;
            CategoryId = categoryId;
        }

        // Constructor for creating a new product without specifying ID
        public Product(string name, string description, decimal price, Guid categoryId) : base() // Call the parameterless base constructor
        {
            Name = name;
            Description = description;
            Price = price;
            CategoryId = categoryId;
        }

        // Protected constructor for EF or internal use
        public Product() : base() // Call the parameterless base constructor
        {
        }

        public Guid Id { get; set; } // Optionally, keep this settable for compatibility with EF
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public double Rating { get; set; }
        public bool IsNew { get; set; }
        public int Stock { get; set; }
        public bool InStock { get; set; }
        public List<ProductImage>? ProductImages { get; set; }
        public List<ProductAsset>? ProductAssets { get; set; }
        public List<ProductReview>? ProductReviews { get; set; }
        public string? Brand { get; set; } = string.Empty;
        public string? Type { get; set; } = string.Empty;
        public Guid CategoryId { get; set; }
        public Category? Category { get; set; }
        public string? BlobName { get; internal set; }
    }
}
