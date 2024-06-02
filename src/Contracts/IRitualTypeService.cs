using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.DTOs;

namespace RitualWorks.Services
{
    public interface IRitualTypeService
    {
        Task<IEnumerable<RitualTypeDto>> GetRitualTypesAsync();
    }
}
