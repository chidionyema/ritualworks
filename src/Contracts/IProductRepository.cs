using System;
using RitualWorks.Db;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RitualWorks.Contracts
{
    public interface IProductRepository
    {
        Task<IEnumerable<Product>> GetProductsAsync();
        Task<Product> GetProductByIdAsync(Guid id);
        Task AddProductAsync(Product product);
        Task UpdateProductAsync(Product product);
        Task DeleteProductAsync(Guid id);
        Task AddProductImageAsync(ProductImage productImage);
    }
}

