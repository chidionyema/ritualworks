using System;
using RitualWorks.Db;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RitualWorks.Contracts
{
    public interface IProductRepository
    {
        Task<IEnumerable<Product>> GetProductsAsync(int page, int pageSize);
        Task<Product> GetProductByIdAsync(Guid id);
        Task<IEnumerable<Product>> GetProductsByCategoryAsync(Guid categoryId, int page, int pageSize);
        Task AddProductAsync(Product product);
        Task UpdateProductAsync(Product product);
        Task DeleteProductAsync(Guid id);
        Task AddProductImageAsync(ProductImage productImage);
        Task AddProductAssetAsync(ProductAsset productAsset);
        Task<List<Product>> GetProductsByIdsAsync(List<Guid> productIds);

         
    }
}

