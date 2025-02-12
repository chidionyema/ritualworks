using System;

namespace haworks.Dto
{
    public class ProductMetadataDto
    {
        public Guid Id { get; set; }
        public string KeyName { get; set; } = string.Empty;
        public string KeyValue { get; set; } = string.Empty;
    }
}
