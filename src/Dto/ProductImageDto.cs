 
  using System;
  namespace haworks.Dto
{
     public class ProductImageDto
    {
        public Guid Id { get; set; }
        public string Url { get; set; }
        public string BlobName { get; set; }
        public Guid ProductId { get; set; }
    }

 }
