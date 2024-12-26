using AutoMapper;
using haworks.Db;
using haworks.Dto;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Map Product to ProductDto, including Contents
        CreateMap<Product, ProductDto>()
            .ForMember(dest => dest.Contents, opt => opt.MapFrom(src => src.Contents));

        // Map ProductCreateDto to Product, ignoring Contents (handled separately)
        CreateMap<ProductCreateDto, Product>()
            .ForMember(dest => dest.Contents, opt => opt.Ignore());

        // Map Category to CategoryDto
        CreateMap<Category, CategoryDto>().ReverseMap();

        // Map Content to ContentDto
        CreateMap<Content, ContentDto>().ReverseMap();
    }
}
