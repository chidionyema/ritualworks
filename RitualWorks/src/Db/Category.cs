using System;
using System.Collections.Generic;

namespace RitualWorks.Db
{
    public class Category : AuditableEntity
    {
        // Constructor to be used by Entity Framework for seeding data
        public Category(Guid id, string name) : base(id) // Explicitly calling the base constructor with the id parameter
        {
            Id = id != Guid.Empty ? id : throw new ArgumentException("Id cannot be an empty GUID.", nameof(id)); // Check for invalid GUID
            Name = name;
        }

        // Alternative constructor for other purposes
        public Category(string name) : base() // Explicitly calling the parameterless base constructor
        {
            Id = Guid.NewGuid(); // Ensure a new GUID is generated if not provided
            Name = name;
        }

        public Category(Guid id, ICollection<Product> products) : base(id) // Explicitly calling the base constructor with the id parameter
        {
            Id = id != Guid.Empty ? id : throw new ArgumentException("Id cannot be an empty GUID.", nameof(id)); // Check for invalid GUID
            Products = products;
        }

        // Protected constructor for Entity Framework
        public Category() : base() // Explicitly calling the parameterless base constructor
        {
            Id = Guid.NewGuid(); // Ensure a valid GUID is always assigned
        }

        public Guid Id { get; init; } = Guid.NewGuid(); // Ensure the Id is always initialized with a new GUID
        public ICollection<Product>? Products { get; private set; } = new List<Product>();
        public string Name { get; set; } = string.Empty;
    }
}
