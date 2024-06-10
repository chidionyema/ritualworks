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
        public long Stock { get; set; }
        public ICollection<ProductImage>? ProductImages { get; set; }
        public Guid CategoryId { get; set; }
        public Category? Category { get; set; }
    }

    public class ProductImage
    {
        public Guid Id { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }
    }

    public class Category
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ICollection<Product>? Products { get; set; }
    }

}

