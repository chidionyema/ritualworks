using System.Collections.Generic;
using AutoMapper;
using RitualWorks.Controllers;
using RitualWorks.Db;
using System.Linq;

namespace RitualWorks
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Product, ProductDto>()
                .ForMember(dest => dest.ImageUrls, opt => opt.MapFrom(src => src.ProductImages != null ? src.ProductImages.Select(pi => pi.Url).ToList() : new List<string>()))
                .ReverseMap();

            CreateMap<ProductReview, ProductReviewDto>().ReverseMap();
            CreateMap<Category, CategoryDto>().ReverseMap();
        }
    }
}
