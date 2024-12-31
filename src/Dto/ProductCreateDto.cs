using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace haworks.Dto
{
    public class ProductCreateDto
    {
        public string Name { get; set; }
        public string Description { get; set; }  = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public double Rating { get; set; }
        public bool IsNew { get; set; }
        public bool InStock { get; set; }
        public string Brand { get; set; }  = string.Empty;
        public string Type { get; set; }  = string.Empty;
        public Guid CategoryId { get; set; }
        public List<IFormFile> Images { get; set; }
        public List<IFormFile> Assets { get; set; }
    }
}
