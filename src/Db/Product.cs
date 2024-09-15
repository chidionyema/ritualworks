using System;
using System.Collections.Generic;

namespace RitualWorks.Db
{
    public class Product
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public double Rating { get; set; }
        public bool IsNew { get; set; }        
        public int Stock { get; set; }
        public bool InStock { get; set; }
        public ICollection<ProductImage>? ProductImages { get; set; }
        public ICollection<ProductAsset>? ProductAssets { get; set; }
        public ICollection<ProductReview>?  ProductReviews { get; set; }
        public string? Brand { get; set; } = string.Empty;
        public string? Type { get; set; } = string.Empty;
        public Guid CategoryId { get; set; }
        public Category? Category { get; set; }
        public string? BlobName { get; internal set; }
    }

}

