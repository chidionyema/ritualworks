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
        public string Url { get; set; } = string.Empty;
        public string BlobName { get; set; } = string.Empty;
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }
    }

    public record ProductAsset
    {
        public ProductAsset()
        {
            // Parameterless constructor for EF Core
        }

        public ProductAsset(Guid id, Guid productId, Product product)
        {
            Id = id;
            ProductId = productId;
            Product = product;
        }

        public Guid Id { get; set; }
        public string AssetUrl { get; set; } = string.Empty;
        public string BlobName { get; set; } = string.Empty;
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }
    }



    public record ProductReview
    {
        public ProductReview()
        {
            // Parameterless constructor for EF Core
        }

        public ProductReview(string user, string comment, double rating)
        {
            User = user;
            Comment = comment;
            Rating = rating;
        }

        public Guid Id { get; set; }
        public string User { get; set; }
        public string Comment { get; set; } = string.Empty;
        public double Rating { get; set; }

        public Guid ProductId { get; set; }
        public Product? Product { get; set; }
    }
}

