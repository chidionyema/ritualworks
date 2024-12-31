using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using haworks.Contracts;
using haworks.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace haworks.Repositories
{
    public class CategoryRepository : ICategoryRepository
    {
        private readonly haworksContext _context;
        private readonly ILogger<CategoryRepository> _logger;

        public CategoryRepository(haworksContext context, ILogger<CategoryRepository> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IEnumerable<Category>> GetCategoriesAsync()
        {
            try
            {
                _logger.LogInformation("Fetching all categories with no tracking.");
                return await _context.Categories
                    .AsNoTracking() // Use no tracking for read-only operations
                    .OrderBy(c => c.Name)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching categories.");
                throw;
            }
        }

        public async Task<Category> GetCategoryByIdAsync(Guid id)
        {
            try
            {
                _logger.LogInformation("Fetching category with ID: {CategoryId} using no tracking.", id);
                var category = await _context.Categories
                    .AsNoTracking() // Use no tracking for read-only operations
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (category == null)
                {
                    _logger.LogWarning("Category with ID {CategoryId} not found.", id);
                }

                return category;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching category with ID {CategoryId}.", id);
                throw;
            }
        }

        public async Task AddCategoryAsync(Category category)
        {
            try
            {
                if (category == null)
                {
                    throw new ArgumentNullException(nameof(category), "Category cannot be null.");
                }

                _logger.LogInformation("Adding a new category: {CategoryName}.", category.Name);
                await _context.Categories.AddAsync(category);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while adding a new category.");
                throw;
            }
        }

        public async Task SaveChangesAsync()
        {
            try
            {
                _logger.LogInformation("Saving changes to the database.");
                await _context.SaveChangesAsync();
                _logger.LogInformation("Database changes saved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while saving changes to the database.");
                throw;
            }
        }
    }
}
