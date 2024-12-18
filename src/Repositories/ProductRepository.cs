﻿using System;
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
                .Include(p => p.ProductImages)
                .Include(p => p.ProductAssets)
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<Product> GetProductByIdAsync(Guid id)
        {
            return await _context.Products
                .Include(p => p.ProductImages)
                .Include(p => p.ProductAssets)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<IEnumerable<Product>> GetProductsByCategoryAsync(Guid categoryId, int page, int pageSize)
        {
            return await _context.Products
                .Include(p => p.ProductImages)
                .Include(p => p.ProductAssets)
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

        public async Task AddProductImagesAsync(IEnumerable<ProductImage> images)
        {
            await _context.ProductImages.AddRangeAsync(images);
        }

        public void RemoveProductImages(IEnumerable<ProductImage> images)
        {
            _context.ProductImages.RemoveRange(images);
        }

        public async Task AddProductAssetsAsync(IEnumerable<ProductAsset> assets)
        {
            await _context.ProductAssets.AddRangeAsync(assets);
        }

        public void RemoveProductAssets(IEnumerable<ProductAsset> assets)
        {
            _context.ProductAssets.RemoveRange(assets);
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
