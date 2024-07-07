using System;
using System.Collections.Generic;

namespace RitualWorks.Db
{
    public record Category
    {
        public Category(Guid id)
        {
            Id = id;
        }

        public Category(Guid id, string name)
        {
            Id = id;
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

