using System.Threading.Tasks;
using RitualWorks.Db;

namespace RitualWorks.Contracts
{
    public interface IUserRepository
    {
        Task<User?> GetUserByIdAsync(string userId);
    }
}