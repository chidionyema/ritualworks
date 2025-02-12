using AutoMapper;
using haworks.Db;
using haworks.Dto;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Product -> ProductDto
        CreateMap<Product, ProductDto>()
            .ForMember(dest => dest.Contents, opt => opt.MapFrom(src => src.Contents))
            // Map metadata as well
            .ForMember(dest => dest.Metadata, opt => opt.MapFrom(src => src.Metadata));

        // Content -> ContentDto
        CreateMap<Content, ContentDto>().ReverseMap();

        // NEW: ProductMetadata -> ProductMetadataDto
        CreateMap<ProductMetadata, ProductMetadataDto>().ReverseMap();

        // Category -> CategoryDto
        CreateMap<Category, CategoryDto>().ReverseMap();

        // ProductCreateDto -> Product
        CreateMap<ProductCreateDto, Product>()
            .ForMember(dest => dest.Contents, opt => opt.Ignore());
    }
}
