using Microsoft.AspNetCore.Mvc;
using RitualWorks.Services;
using RitualWorks.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RitualWorks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RitualController : ControllerBase
    {
        private readonly IRitualService _ritualService;

        public RitualController(IRitualService ritualService)
        {
            _ritualService = ritualService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<RitualDto>>> GetRituals()
        {
            var rituals = await _ritualService.GetAllRitualsAsync();
            return Ok(rituals);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RitualDto>> GetRitual(int id)
        {
            var ritual = await _ritualService.GetRitualByIdAsync(id);
            if (ritual == null)
            {
                return NotFound();
            }
            return Ok(ritual);
        }

        [HttpPost]
        public async Task<ActionResult<RitualDto>> CreateRitual(CreateRitualDto ritualDto)
        {
            var createdRitual = await _ritualService.CreateRitualAsync(ritualDto);
            return CreatedAtAction(nameof(GetRitual), new { id = createdRitual.Id }, createdRitual);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRitual(int id, CreateRitualDto ritualDto)
        {
            var updatedRitual = await _ritualService.UpdateRitualAsync(id, ritualDto);
            if (updatedRitual == null)
            {
                return NotFound();
            }
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRitual(int id)
        {
            var result = await _ritualService.DeleteRitualAsync(id);
            if (!result)
            {
                return NotFound();
            }
            return NoContent();
        }
    }
}
