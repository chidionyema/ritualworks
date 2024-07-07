using System;
using AutoMapper;
using RitualWorks.Controllers;
using RitualWorks.Db;

namespace RitualWorks
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Product, ProductDto>().ReverseMap();
            CreateMap<Category, CategoryDto>().ReverseMap();
        }
    }
}

