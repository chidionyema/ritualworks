using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using haworks.Contracts;
using haworks.Db;
using Microsoft.EntityFrameworkCore;

namespace haworks.Repositories
{
    public class CategoryRepository : ICategoryRepository
    {
        private readonly haworksContext _context;

        public CategoryRepository(haworksContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
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

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
