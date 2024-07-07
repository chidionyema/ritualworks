using System;

namespace RitualWorks.Db
{
    public record ProductImage
    {
        public ProductImage()
        {
            // Parameterless constructor for EF Core
        }

        public ProductImage(Guid id, Guid productId, Product product)
        {
            Id = id;
            ProductId = productId;
            Product = product;
        }

        public Guid Id { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }
    }

}

