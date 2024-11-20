 
using System;
 namespace haworks.Dto
{
   public class ProductAssetDto
    {
        public Guid Id { get; set; }
        public string AssetUrl { get; set; }
        public string BlobName { get; set; }
        public Guid ProductId { get; set; }
    }
}
