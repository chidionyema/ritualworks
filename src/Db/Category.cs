using System;
using System.Collections.Generic;

namespace RitualWorks.Db
{
    public record Category
    {
        // Constructor to be used by Entity Framework for seeding data
        public Category(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        // Alternative constructors for other purposes
        public Category(string name)
        {
            Id = Guid.NewGuid(); // Generate a new Guid if not provided
            Name = name;
        }

        public Category(Guid id, ICollection<Product> products)
        {
            Id = id;
            Products = products;
        }

        public Guid Id { get; init; }
        public ICollection<Product>? Products { get; private set; } = new List<Product>();
        public string Name { get; set; } = string.Empty;
    }
}
