 
  using System;
  using System.Collections.Generic;
  namespace haworks.Dto
{
    public class ProductDto
        {
            public Guid Id { get; set; }
            public string Name { get; set; }  = string.Empty;
            public string Description { get; set; }  = string.Empty;
            public decimal Price { get; set; }
            public int Stock { get; set; }
            public double Rating { get; set; }
            public bool IsNew { get; set; }
            public bool InStock { get; set; }
            public string Brand { get; set; }  = string.Empty;
            public string Type { get; set; }
            public Guid CategoryId { get; set; }
            public List<ContentDto> Contents { get; set; }
  
        }
 }
