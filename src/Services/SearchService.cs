using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nest;
using RitualWorks.Controllers;
using RitualWorks.Db;

namespace RitualWorks.Services
{
    public class SearchService
    {
        private readonly IElasticClient _client;
        private readonly ILogger<SearchService> _logger;

        public SearchService(IElasticClient client, ILogger<SearchService> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<List<RitualDto>> SearchRitualsAsync(string query)
        {
            try
            {
                var response = await _client.SearchAsync<Ritual>(s => s
                    .Query(q => q
                        .MultiMatch(m => m
                            .Fields(f => f
                                .Field(p => p.Title)
                                .Field(p => p.Description))
                            .Query(query))));

                if (response.IsValid)
                {
                    return response.Documents.Select(ritual => new RitualDto
                    {
                        Id = ritual.Id,
                        Title = ritual.Title,
                        Description = ritual.Description,
                        Preview = ritual.Preview,
                        FullTextContent = ritual.FullTextContent,
                        MediaUrl = ritual.MediaUrl,
                        TokenAmount = ritual.TokenAmount,
                        RitualType = ritual.RitualType,
                        IsLocked = ritual.IsLocked,
                        IsExternalMediaUrl = ritual.IsExternalMediaUrl,
                        IsProduct = ritual.IsProduct,
                        Rating = ritual.Rating
                    }).ToList();
                }

                _logger.LogError("Failed to search rituals: {0}", response.OriginalException?.Message);
                return new List<RitualDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error searching rituals: {0}", ex.Message);
                return new List<RitualDto>();
            }
        }

        public async Task<List<ProductDto>> SearchProductsAsync(string query)
        {
            try
            {
                var response = await _client.SearchAsync<Product>(s => s
                    .Query(q => q
                        .MultiMatch(m => m
                            .Fields(f => f
                                .Field(p => p.Name)
                                .Field(p => p.Description))
                            .Query(query))));

                if (response.IsValid)
                {
                    return response.Documents.Select(product => new ProductDto
                    {
                        Id = product.Id,
                        Name = product.Name,
                        Description = product.Description,
                        Price = product.Price,
                        Stock = product.Stock,
                        CategoryId = product.CategoryId,
                        ImageUrls = product.ProductImages?.Select(pi => pi.Url).ToList() // Map ProductImage to List of URLs
                    }).ToList();
                }

                _logger.LogError("Failed to search products: {0}", response.OriginalException?.Message);
                return new List<ProductDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error searching products: {0}", ex.Message);
                return new List<ProductDto>();
            }
        }
    }
}
