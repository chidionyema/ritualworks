 
  using System;
  using System.Collections.Generic;
  namespace haworks.Dto
{
    public class ProductDto
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public decimal Price { get; set; }
            public int Stock { get; set; }
            public double Rating { get; set; }
            public bool IsNew { get; set; }
            public bool InStock { get; set; }
            public string Brand { get; set; }
            public string Type { get; set; }
            public Guid CategoryId { get; set; }
            public List<ProductImageDto> ProductImages { get; set; }
            public List<ProductAssetDto> ProductAssets { get; set; }
        }
 }
