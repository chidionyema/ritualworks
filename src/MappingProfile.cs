using AutoMapper;
using RitualWorks.Db;
using static RitualWorks.Controllers.ProductsController;

namespace RitualWorks
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            // Mapping for Product and ProductDto
            CreateMap<Product, ProductDto>().ReverseMap();

            // Mapping for ProductReview and ProductReviewDto
            CreateMap<ProductReview, ProductReviewDto>().ReverseMap();

            // Mapping for Category and CategoryDto
            CreateMap<Category, CategoryDto>().ReverseMap();

            // Add the missing mapping for ProductCreateDto to Product
            CreateMap<ProductCreateDto, Product>()
                .ForMember(dest => dest.Id, opt => opt.Ignore()) // Ignore the ID, since it's generated
                .ForMember(dest => dest.ProductImages, opt => opt.Ignore()) // These will be handled separately
                .ForMember(dest => dest.ProductAssets, opt => opt.Ignore()) // These will be handled separately
                .ForMember(dest => dest.Category, opt => opt.Ignore()); // Category object will be handled elsewhere
        }
    }
}
