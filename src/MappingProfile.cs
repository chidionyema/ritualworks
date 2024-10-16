using AutoMapper;
using RitualWorks.Contracts;
using RitualWorks.Db;
using RitualWorks.Controllers;
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Product mappings
        CreateMap<Product, ProductDto>()
            .ForMember(dest => dest.ProductImages, opt => opt.MapFrom(src => src.ProductImages))
            .ForMember(dest => dest.ProductAssets, opt => opt.MapFrom(src => src.ProductAssets));

        CreateMap<ProductCreateDto, Product>()
            .ForMember(dest => dest.ProductImages, opt => opt.Ignore()) // Handled separately
            .ForMember(dest => dest.ProductAssets, opt => opt.Ignore()); // Handled separately

        // Category mappings
        CreateMap<Category, CategoryDto>().ReverseMap();

        // ProductImage mappings
        CreateMap<ProductImage, ProductImageDto>().ReverseMap();

        // ProductAsset mappings
        CreateMap<ProductAsset, ProductAssetDto>().ReverseMap();
    }
}
