using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RitualWorks.Db;
using RitualWorks.DTOs;

namespace RitualWorks.Services
{
    public interface IRitualService
    {
        Task<RitualDto> CreateRitualAsync(CreateRitualDto ritualDto, Stream mediaStream = null);
        Task<RitualDto?> UpdateRitualAsync(int id, CreateRitualDto ritualDto, Stream? mediaStream = null);
        Task<RitualDto?> GetRitualByIdAsync(int id);
        Task<IEnumerable<RitualDto>> GetAllRitualsAsync();
        Task<bool> LockRitualAsync(int id);
        Task<bool> RateRitualAsync(int id, double rating);
        Task<IEnumerable<RitualDto>> SearchRitualsAsync(string query, RitualTypeEnum? type);
    }

}
