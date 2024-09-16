using System;
using RitualWorks.Contracts;
using RitualWorks.Db;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;

namespace RitualWorks.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly RitualWorksContext _context;

        public ProductRepository(RitualWorksContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Product>> GetProductsAsync(int page, int pageSize)
        {
            var products = await _context.Products
                             .OrderBy(p => p.Name)
                             .Skip((page - 1) * pageSize)
                             .Take(pageSize)
                             .ToListAsync();

            Console.WriteLine("Products Retrieved: " + string.Join(", ", products.Select(p => p.Name)));
            return products;
        }

        public async Task<IEnumerable<Product>> GetProductsByCategoryAsync(Guid categoryId, int page, int pageSize)
        {
            return await _context.Products
                                 .Where(p => p.CategoryId == categoryId)
                                 .OrderBy(p => p.Name) // Ensure consistent order
                                 .Skip((page - 1) * pageSize)
                                 .Take(pageSize)
                                 .ToListAsync();
        }

        public async Task AddProductAssetAsync(ProductAsset productAsset)
        {
            await _context.ProductAssets.AddAsync(productAsset);
            await _context.SaveChangesAsync();
        }

        public async Task<Product> GetProductByIdAsync(Guid id)
        {
            return await _context.Products.Include(p => p.ProductImages).FirstOrDefaultAsync(p => p.Id == id);
        }

         public async Task<List<Product>> GetProductsByIdsAsync(List<Guid> productIds)
        {
            return await _context.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
        }
        public async Task AddProductAsync(Product product)
        {
            await _context.Products.AddAsync(product);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateProductAsync(Product product)
        {
            _context.Products.Update(product);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteProductAsync(Guid id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product != null)
            {
                _context.Products.Remove(product);
                await _context.SaveChangesAsync();
            }
        }

        public async Task AddProductImageAsync(ProductImage productImage)
        {
            await _context.ProductImages.AddAsync(productImage);
            await _context.SaveChangesAsync();
        }
    }
}

