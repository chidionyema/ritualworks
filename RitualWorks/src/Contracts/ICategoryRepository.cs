using System;
using RitualWorks.Db;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RitualWorks.Contracts
{
    public interface ICategoryRepository
    {
        Task<IEnumerable<Category>> GetCategoriesAsync();
        Task<Category> GetCategoryByIdAsync(Guid id);
        Task AddCategoryAsync(Category category);
    }
}

