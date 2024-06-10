using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.Controllers;

namespace RitualWorks.Services
{
    public interface IRitualTypeService
    {
        Task<IEnumerable<RitualTypeDto>> GetRitualTypesAsync();
    }
}
