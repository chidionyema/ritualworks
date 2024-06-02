using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.DTOs;

namespace RitualWorks.Contracts
{
    public interface IDonationService
    {
        Task<DonationDto> CreateDonationAsync(CreateDonationDto createDonationDto);
        Task<IEnumerable<DonationDto>> GetDonationsByPetitionIdAsync(int petitionId);
        Task<IEnumerable<DonationDto>> GetDonationsByRitualIdAsync(int ritualId);
        Task<DonationDto?> GetDonationByIdAsync(int id);
    }

}

