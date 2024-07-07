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

}

