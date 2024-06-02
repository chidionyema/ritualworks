using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.DTOs;

namespace RitualWorks.Contracts
{
    public interface IPetitionService
    {
        Task<PetitionDto> CreatePetitionAsync(CreatePetitionDto createPetitionDto);
        Task<IEnumerable<PetitionDto>> GetPetitionsByRitualIdAsync(int ritualId);
        Task<PetitionDto?> GetPetitionByIdAsync(int id);
    }

}

