using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.Db;

namespace RitualWorks.Contracts
{
    public interface IDonationRepository
    {
        Task<Donation> CreateDonationAsync(Donation donation);
        Task<Donation?> GetDonationByIdAsync(int id);
        Task<IEnumerable<Donation>> GetDonationsByPetitionIdAsync(int petitionId);
        Task<IEnumerable<Donation>> GetDonationsByRitualIdAsync(int ritualId);
    }
}
