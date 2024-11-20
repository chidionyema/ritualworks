using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using haworks.Db;

namespace haworks.Contracts
{
    public interface IProductRepository
    {
        Task<IEnumerable<Product>> GetProductsAsync(int page, int pageSize);
        Task<Product> GetProductByIdAsync(Guid id);
        Task<IEnumerable<Product>> GetProductsByCategoryAsync(Guid categoryId, int page, int pageSize);
        Task AddProductAsync(Product product);
        Task UpdateProductAsync(Product product);
        Task DeleteProductAsync(Guid id);
        Task AddProductImagesAsync(IEnumerable<ProductImage> images);
        void RemoveProductImages(IEnumerable<ProductImage> images);
        Task AddProductAssetsAsync(IEnumerable<ProductAsset> assets);
        void RemoveProductAssets(IEnumerable<ProductAsset> assets);
        Task SaveChangesAsync();
        Task<List<Product>> GetProductsByIdsAsync(List<Guid> productIds);
    }
}
