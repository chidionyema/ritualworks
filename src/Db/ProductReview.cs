using System;

namespace haworks.Db
{
    public class ProductReview : AuditableEntity
    {
        // Parameterless constructor for EF Core
        public ProductReview() { }

        // Constructor to initialize review details
        public ProductReview(string user, string comment, double rating)
        {
            User = user;
            Comment = comment;
            Rating = rating;
        }

        // Name or identifier for the reviewer
        public string User { get; set; } = string.Empty;

        // Review comment
        public string Comment { get; set; } = string.Empty;

        // Rating value
        public double Rating { get; set; }

        // Foreign key for the associated product
        public Guid ProductId { get; set; }

        // Navigation property to the Product
        public Product? Product { get; set; }
    }
}
