using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
// using Microsoft.Extensions.Caching.Distributed;  // Distributed cache is commented out
using haworks.Contracts;
using haworks.Db;
using haworks.Repositories.Base;

namespace haworks.Repositories
{
    public class CategoryRepository : BaseRepository<Category, haworksContext>, ICategoryRepository
    {
        // Cache keys
        private const string CATEGORY_LIST_KEY = "categories_list";
        private const string CATEGORY_KEY = "category_{0}";

        public CategoryRepository(
            haworksContext context,
            ILogger<CategoryRepository> logger,
            IMemoryCache memoryCache,
            /*IDistributedCache distributedCache*/ object distributedCachePlaceholder = null)
            : base(context, logger, memoryCache, distributedCachePlaceholder)
        {
        }

        public async Task<IEnumerable<Category>> GetCategoriesAsync()
        {
            string cacheKey = CATEGORY_LIST_KEY;

            return await GetFromCacheAsync<IEnumerable<Category>>(cacheKey, async () =>
            {
                Logger.LogInformation("Fetching all categories from the database.");
                return await Context.Categories
                    .AsNoTracking()
                    .OrderBy(c => c.Name)
                    .ToListAsync();
            }) ?? Enumerable.Empty<Category>();
        }

        public async Task<Category> GetCategoryByIdAsync(Guid id)
        {
            string cacheKey = string.Format(CATEGORY_KEY, id);

            return await GetFromCacheAsync<Category>(cacheKey, async () =>
            {
                Logger.LogInformation("Fetching category with ID {CategoryId} from the database.", id);
                return await Context.Categories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == id);
            });
        }

        public async Task AddCategoryAsync(Category category)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));

            try
            {
                Logger.LogInformation("Adding a new category: {CategoryName}.", category.Name);
                await Context.Categories.AddAsync(category);
                await SaveChangesAsync();

                // Update cache
                await SetCacheValuesAsync(string.Format(CATEGORY_KEY, category.Id), category);
                await RemoveFromCacheAsync(CATEGORY_LIST_KEY); // Invalidate list cache
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error occurred while adding a new category.");
                throw;
            }
        }

        public async Task UpdateCategoryAsync(Category category)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));

            try
            {
                Logger.LogInformation("Updating category with ID {CategoryId}.", category.Id);
                Context.Categories.Attach(category);
                Context.Entry(category).State = EntityState.Modified;
                await SaveChangesAsync();

                // Update cache
                await SetCacheValuesAsync(string.Format(CATEGORY_KEY, category.Id), category);
                await RemoveFromCacheAsync(CATEGORY_LIST_KEY); // Invalidate list cache
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error occurred while updating the category with ID {CategoryId}.", category.Id);
                throw;
            }
        }

        public async Task DeleteCategoryAsync(Guid id)
        {
            try
            {
                Logger.LogInformation("Deleting category with ID {CategoryId}.", id);
                var category = await Context.Categories.FindAsync(id);
                if (category == null)
                {
                    Logger.LogWarning("Category with ID {CategoryId} not found.", id);
                    return;
                }

                Context.Categories.Remove(category);
                await SaveChangesAsync();

                // Remove cache entries
                await RemoveFromCacheAsync(string.Format(CATEGORY_KEY, id));
                await RemoveFromCacheAsync(CATEGORY_LIST_KEY); // Invalidate list cache
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error occurred while deleting the category with ID {CategoryId}.", id);
                throw;
            }
        }
    }
}
