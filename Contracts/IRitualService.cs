using RitualWorks.Db;
using RitualWorks.DTOs;

namespace RitualWorks.Services
{
    public interface IRitualService
    {
        Task<IEnumerable<RitualDto>> GetAllRitualsAsync();
        Task<RitualDto> GetRitualByIdAsync(int id);
        Task<RitualDto> CreateRitualAsync(CreateRitualDto ritualDto);
        Task<RitualDto> UpdateRitualAsync(int id, CreateRitualDto ritualDto);
        Task<bool> DeleteRitualAsync(int id);
    }

}

