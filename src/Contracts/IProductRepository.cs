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
        Task<Product?> GetProductByIdAsync(Guid id, bool includeCategory = false, bool includeContents = false, bool includeMetadata = false);
        Task<IEnumerable<Product>> GetProductsByCategoryAsync(Guid categoryId, int page, int pageSize);
        Task AddProductAsync(Product product);
        Task UpdateProductAsync(Product product);
        Task UpdateProductStockAsync(Guid productId, int quantity);
        Task DeleteProductAsync(Guid id);
        Task SaveChangesAsync();
        Task<List<Product>> GetProductsByIdsAsync(List<Guid> productIds);

        Task<bool> ValidateStockAsync(Guid productId, int quantity);

         /// <summary>
        /// Decrements the stock of the specified product.
        /// </summary>
        Task<bool> DecrementStockAsync(Guid productId, int quantity);
    }
}
