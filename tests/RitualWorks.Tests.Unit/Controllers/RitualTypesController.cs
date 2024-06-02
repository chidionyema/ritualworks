using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RitualWorks.DTOs;
using RitualWorks.Services;

namespace RitualWorks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RitualTypesController : ControllerBase
    {
        private readonly IRitualTypeService _ritualTypeService;

        public RitualTypesController(IRitualTypeService ritualTypeService)
        {
            _ritualTypeService = ritualTypeService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<RitualTypeDto>>> GetRitualTypes()
        {
            var ritualTypes = await _ritualTypeService.GetRitualTypesAsync();
            return Ok(ritualTypes);
        }
    }
}
