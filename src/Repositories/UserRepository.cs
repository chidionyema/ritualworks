using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using haworks.Contracts;
using haworks.Db;

namespace haworks.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly haworksContext _context;

        public UserRepository(haworksContext context)
        {
            _context = context;
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId);
        }
    }
}
