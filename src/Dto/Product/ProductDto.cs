 
  using System;
  using System.Collections.Generic;
  namespace haworks.Dto
{
    public class ProductDto
        {
            public Guid Id { get; set; }
            public string Name { get; set; }  = string.Empty;
            public string Headline { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string ShortDescription { get; set; } = string.Empty;
            public string Description { get; set; }  = string.Empty;
            public decimal UnitPrice { get; set; }
            public int Stock { get; set; }
            public bool IsListed { get; set; }
            public bool IsFeatured { get; set; }
            public double Rating { get; set; }
            public bool IsInStock { get; set; }
            public string Brand { get; set; }  = string.Empty;
            public string Type { get; set; }
            public Guid CategoryId { get; set; }
            public List<ContentDto> Contents { get; set; } = new List<ContentDto>();
            public List<ProductMetadataDto> Metadata { get; set; } = new List<ProductMetadataDto>();
  
        }
 }
