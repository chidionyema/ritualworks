using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.Controllers;
using RitualWorks.Db;

namespace RitualWorks.Services
{
    public interface IRitualService
    {
        Task<RitualDto> CreateRitualAsync(CreateRitualDto ritualDto);
        Task<RitualDto?> UpdateRitualAsync(int id, CreateRitualDto ritualDto);
        Task<RitualDto?> GetRitualByIdAsync(int id);
        Task<IEnumerable<RitualDto>> GetAllRitualsAsync();
        Task<bool> LockRitualAsync(int id);
        Task<bool> RateRitualAsync(int id, double rating);
    }
}
