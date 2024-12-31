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
        Task UpdateProductStockAsync(Guid productId, int quantity);
        Task DeleteProductAsync(Guid id);
        Task SaveChangesAsync();
        Task<List<Product>> GetProductsByIdsAsync(List<Guid> productIds);
    }
}
