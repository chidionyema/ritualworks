using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nest;
using haworks.Db;
using haworks.Controllers;
using haworks.Dto;
namespace haworks.Services
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
                        Price = product.UnitPrice,
                        Stock = product.Stock,
                        CategoryId = product.CategoryId,
                      //  ImageUrls = product.ProductImages?.Select(pi => pi.Url).ToList() // Map ProductImage to List of URLs
                    }).ToList();
                }

                _logger.LogError("Failed to search products: {0}", response.OriginalException?.Message);
                return [];
            }
            catch (Exception ex)
            {
                _logger.LogError("Error searching products: {0}", ex.Message);
                return [];
            }
        }
    }
}
