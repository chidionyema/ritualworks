using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.Db;

namespace RitualWorks.Contracts
{
    public interface IPetitionRepository
    {
        Task<Petition> CreatePetitionAsync(Petition petition);
        Task<Petition?> GetPetitionByIdAsync(int id);
        Task<IEnumerable<Petition>> GetPetitionsByRitualIdAsync(int ritualId);
    }
}
