using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using haworks.Contracts;
using haworks.Db;
using haworks.Repositories.Base;

namespace haworks.Repositories
{
    public class ProductRepository : BaseRepository<Product, haworksContext>, IProductRepository
    {
        // Cache keys
        private const string PRODUCTS_LIST_KEY = "products_list_p{0}_s{1}";
        private const string PRODUCT_KEY = "product_{0}";
        private const string CATEGORY_PRODUCTS_KEY = "category_{0}_products_p{1}_s{2}";
        private const string PRODUCTS_BY_IDS_KEY = "products_by_ids_{0}";

        public ProductRepository(
            haworksContext context,
            ILogger<ProductRepository> logger,
            Microsoft.Extensions.Caching.Memory.IMemoryCache memoryCache,
            /*IDistributedCache distributedCache*/ object distributedCachePlaceholder = null)
            : base(context, logger, memoryCache, distributedCachePlaceholder)
        {
        }

        public async Task<IEnumerable<Product>> GetProductsAsync(int page, int pageSize)
        {
            page = NormalizePage(page);
            pageSize = NormalizePageSize(pageSize);
            string cacheKey = string.Format(PRODUCTS_LIST_KEY, page, pageSize);

            return await GetFromCacheAsync<IEnumerable<Product>>(cacheKey, async () =>
            {
                return await Context.Products
                    .AsNoTracking()
                    .Include(p => p.Category)
                    .Include(p => p.Contents.Where(c => c.EntityType == nameof(Product)))
                    .OrderBy(p => p.Name)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            }) ?? Enumerable.Empty<Product>();
        }

        public async Task<Product?> GetProductByIdAsync(Guid id,
                                                         bool includeCategory = false,
                                                         bool includeContents = false,
                                                         bool includeMetadata = false)
        {
            string cacheKey = string.Format(PRODUCT_KEY, id);

            return await GetFromCacheAsync<Product>(cacheKey, async () =>
            {
                var query = Context.Products.AsNoTracking().Where(p => p.Id == id);

                if (includeCategory)
                {
                    query = query.Include(p => p.Category);
                }

                if (includeContents)
                {
                    query = query.Include(p => p.Contents.Where(c => c.EntityType == nameof(Product)));
                }

                if (includeMetadata)
                {
                    query = query.Include(p => p.Metadata);
                }

                return await query.FirstOrDefaultAsync();
            });
        }

        public async Task<IEnumerable<Product>> GetProductsByCategoryAsync(Guid categoryId, int page, int pageSize)
        {
            page = NormalizePage(page);
            pageSize = NormalizePageSize(pageSize);
            string cacheKey = string.Format(CATEGORY_PRODUCTS_KEY, categoryId, page, pageSize);

            return await GetFromCacheAsync<IEnumerable<Product>>(cacheKey, async () =>
            {
                return await Context.Products
                    .AsNoTracking()
                    .Include(p => p.Category)
                    .Include(p => p.Contents.Where(c => c.EntityType == nameof(Product)))
                    .Where(p => p.CategoryId == categoryId)
                    .OrderBy(p => p.Name)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            }) ?? Enumerable.Empty<Product>();
        }

        public async Task<List<Product>> GetProductsByIdsAsync(List<Guid> productIds)
        {
            string cacheKey = string.Format(PRODUCTS_BY_IDS_KEY, string.Join("-", productIds.OrderBy(id => id)));

            return await GetFromCacheAsync<List<Product>>(cacheKey, async () =>
            {
                return await Context.Products
                    .AsNoTracking()
                    .Include(p => p.Contents.Where(c => c.EntityType == nameof(Product)))
                    .Where(p => productIds.Contains(p.Id))
                    .ToListAsync();
            }) ?? new List<Product>();
        }

        public async Task AddProductAsync(Product product)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));

            try
            {
                Logger.LogInformation("Adding a new product: {ProductName}.", product.Name);
                await Context.Products.AddAsync(product);
                await SaveChangesAsync();

                await SetCacheValuesAsync(string.Format(PRODUCT_KEY, product.Id), product);
                await InvalidateListCachesAsync(product.CategoryId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error occurred while adding a new product: {ProductName}.", product.Name);
                throw new ApplicationException($"An error occurred while adding the product: {product.Name}.", ex);
            }
        }

        public async Task UpdateProductAsync(Product product)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));

            try
            {
                Logger.LogInformation("Updating product with ID {ProductId}.", product.Id);
                Context.Products.Attach(product);
                Context.Entry(product).State = EntityState.Modified;
                await SaveChangesAsync();

                await SetCacheValuesAsync(string.Format(PRODUCT_KEY, product.Id), product);
                await InvalidateListCachesAsync(product.CategoryId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error occurred while updating product with ID {ProductId}.", product.Id);
                throw new ApplicationException($"An error occurred while updating the product with ID {product.Id}.", ex);
            }
        }

        public async Task UpdateProductStockAsync(Guid productId, int quantity)
        {
            using var transaction = await Context.Database.BeginTransactionAsync();
            try
            {
                var product = await Context.Products.FirstOrDefaultAsync(p => p.Id == productId);
                if (product == null)
                    throw new InvalidOperationException($"Product with ID {productId} not found.");
                if (product.Stock < quantity)
                    throw new InvalidOperationException($"Insufficient stock for Product ID {productId}.");

                product.Stock -= quantity;
                Context.Products.Update(product);
                await SaveChangesAsync();
                await transaction.CommitAsync();

                Logger.LogInformation("Stock updated successfully for Product ID: {ProductId}", productId);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                Logger.LogWarning("Concurrency conflict detected while updating stock for Product ID: {ProductId}", productId);
                throw new InvalidOperationException("The product stock was updated by another operation. Please try again.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Logger.LogError(ex, "An error occurred while updating product stock for Product ID: {ProductId}", productId);
                throw new ApplicationException($"An error occurred while updating stock for Product ID: {productId}.", ex);
            }
        }

        public async Task DeleteProductAsync(Guid id)
        {
            using var transaction = await Context.Database.BeginTransactionAsync();
            try
            {
                var product = await Context.Products
                    .Include(p => p.Contents.Where(c => c.EntityType == nameof(Product)))
                    .FirstOrDefaultAsync(p => p.Id == id);

                if (product == null)
                {
                    Logger.LogWarning("Product with ID {ProductId} not found.", id);
                    throw new InvalidOperationException($"Product with ID {id} not found.");
                }

                Logger.LogInformation("Deleting product with ID {ProductId}.", id);
                Context.Products.Remove(product);
                await SaveChangesAsync();

                await RemoveFromCacheAsync(string.Format(PRODUCT_KEY, id));
                await InvalidateListCachesAsync(product.CategoryId);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Logger.LogError(ex, "An error occurred while deleting product with ID {ProductId}.", id);
                throw new ApplicationException($"An error occurred while deleting the product with ID {id}.", ex);
            }
        }

        private async Task InvalidateListCachesAsync(Guid? categoryId = null)
        {
            var keysToRemove = new List<string> { PRODUCTS_LIST_KEY, PRODUCTS_BY_IDS_KEY };

            if (categoryId.HasValue)
            {
                keysToRemove.Add(string.Format(CATEGORY_PRODUCTS_KEY, categoryId.Value, "*", "*"));
            }

            await RemoveFromCacheAsync(keysToRemove);
        }
    }
}
