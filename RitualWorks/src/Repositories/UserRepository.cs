using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RitualWorks.Contracts;
using RitualWorks.Db;

namespace RitualWorks.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly RitualWorksContext _context;

        public UserRepository(RitualWorksContext context)
        {
            _context = context;
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            return await _context.Users
                .Include(u => u.Rituals)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }
    }
}
