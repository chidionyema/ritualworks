using System.Threading.Tasks;
using haworks.Db;

namespace haworks.Contracts
{  
     public interface IUserRepository {
        Task<User?> GetUserByIdAsync(string userId);
    }
}