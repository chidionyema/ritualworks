using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.Controllers;

namespace RitualWorks.Contracts
{
    public interface IDonationService
    {
        Task<(DonationDto donation, string sessionId)> CreateDonationAsync(CreateDonationDto createDonationDto, string domain);
        Task<IEnumerable<DonationDto>> GetDonationsByPetitionIdAsync(int petitionId);
        Task<IEnumerable<DonationDto>> GetDonationsByRitualIdAsync(int ritualId);
        Task<DonationDto?> GetDonationByIdAsync(int id);
    }

}

