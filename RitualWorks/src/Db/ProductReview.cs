using System;

namespace RitualWorks.Db
{
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

