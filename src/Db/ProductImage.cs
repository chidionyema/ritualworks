using System;

namespace haworks.Db
{
    public record ProductImage
    {
        public ProductImage()
        {
            // Parameterless constructor for EF Core
        }

        public ProductImage(Guid id, Guid productId, string url)
        {
            Id = id;
            ProductId = productId;
            Url = url;
        }

        public Guid Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string BlobName { get; set; } = string.Empty;
        public Guid ProductId { get; set; }
        public Product? Product { get; set; }
    }
}

