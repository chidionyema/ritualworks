using System;

namespace RitualWorks.Db
{
    public record ProductAsset
    {
        public ProductAsset()
        {
            // Parameterless constructor for EF Core
        }

        public ProductAsset(Guid id, Guid productId, string url)
        {
            Id = id;
            ProductId = productId;
            AssetUrl = url;
        }

        public Guid Id { get; set; }
        public string AssetUrl { get; set; } = string.Empty;
        public string BlobName { get; set; } = string.Empty;
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }
    }
}

