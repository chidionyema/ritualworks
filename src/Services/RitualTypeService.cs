using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RitualWorks.Controllers;
using RitualWorks.Db;

namespace RitualWorks.Services
{
    public class RitualTypeService : IRitualTypeService
    {
        public Task<IEnumerable<RitualTypeDto>> GetRitualTypesAsync()
        {
            var ritualTypes = ((IEnumerable<RitualTypeEnum>)Enum.GetValues(typeof(RitualTypeEnum)))
                .Select(e => new RitualTypeDto
                {
                    Name = e.ToString(),
                    Value = (int)e
                })
                .ToList();

            return Task.FromResult((IEnumerable<RitualTypeDto>)ritualTypes);
        }
    }
}
