using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using haworks.Db;

namespace haworks.Contracts
{
    public interface IProductRepository
    {
        // Product-related methods
        Task<IEnumerable<Product>> GetProductsAsync(int page, int pageSize);
        Task<Product> GetProductByIdAsync(Guid id);
        Task<IEnumerable<Product>> GetProductsByCategoryAsync(Guid categoryId, int page, int pageSize);
        Task AddProductAsync(Product product);
        Task UpdateProductAsync(Product product);
        Task DeleteProductAsync(Guid id);

        // Content-related methods (generic for images and assets)
        Task AddContentAsync(IEnumerable<Content> contents);
        void RemoveContent(IEnumerable<Content> contents);
        Task<List<Content>> GetContentByProductIdAsync(Guid productId, ContentType contentType);

        // Miscellaneous methods
        Task SaveChangesAsync();
        Task<List<Product>> GetProductsByIdsAsync(List<Guid> productIds);
    }
}
