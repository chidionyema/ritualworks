using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using haworks.Contracts;
using haworks.Db;

namespace haworks.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly haworksContext _context;

        public ProductRepository(haworksContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<IEnumerable<Product>> GetProductsAsync(int page, int pageSize)
        {
            return await _context.Products
                .Include(p => p.Category)
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<Product> GetProductByIdAsync(Guid id)
        {
            return await _context.Products
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<IEnumerable<Product>> GetProductsByCategoryAsync(Guid categoryId, int page, int pageSize)
        {
            return await _context.Products
                .Include(p => p.Category)
                .Where(p => p.CategoryId == categoryId)
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task AddProductAsync(Product product)
        {
            await _context.Products.AddAsync(product);
        }

        public async Task UpdateProductAsync(Product product)
        {
            _context.Products.Update(product);
        }

        public async Task DeleteProductAsync(Guid id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product != null)
            {
                // Remove associated content
                var relatedContent = await _context.Contents
                    .Where(c => c.EntityId == id && c.EntityType == nameof(Product))
                    .ToListAsync();

                _context.Contents.RemoveRange(relatedContent);
                _context.Products.Remove(product);
            }
        }

        public async Task<IEnumerable<Category>> GetCategoriesAsync()
        {
            return await _context.Categories
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<Category> GetCategoryByIdAsync(Guid id)
        {
            return await _context.Categories.FindAsync(id);
        }

        public async Task AddCategoryAsync(Category category)
        {
            await _context.Categories.AddAsync(category);
        }

        public async Task AddContentAsync(IEnumerable<Content> contents)
        {
            await _context.Contents.AddRangeAsync(contents);
        }

        public void RemoveContent(IEnumerable<Content> contents)
        {
            _context.Contents.RemoveRange(contents);
        }

        public async Task<List<Content>> GetContentByProductIdAsync(Guid productId, ContentType contentType)
        {
            return await _context.Contents
                .Where(c => c.EntityId == productId && c.EntityType == nameof(Product) && c.ContentType == contentType)
                .ToListAsync();
        }

        public async Task<List<Product>> GetProductsByIdsAsync(List<Guid> productIds)
        {
            return await _context.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync();
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
