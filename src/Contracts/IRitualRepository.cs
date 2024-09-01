using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.Db;

namespace RitualWorks.Contracts
{
    public interface IRitualRepository
    {
        Task<Ritual> CreateRitualAsync(Ritual ritual);
        Task<Ritual?> GetRitualByIdAsync(int id);
        Task<IEnumerable<Ritual>> GetAllRitualsAsync();
        Task<Ritual?> UpdateRitualAsync(Ritual ritual);
        Task<bool> LockRitualAsync(int id);
        Task<bool> RateRitualAsync(int id, double rating);
    }
}
