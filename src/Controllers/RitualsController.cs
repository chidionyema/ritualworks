using Microsoft.AspNetCore.Mvc;
using RitualWorks.Db;
using RitualWorks.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RitualsController : ControllerBase
    {
        private readonly IRitualService _ritualService;

        public RitualsController(IRitualService ritualService)
        {
            _ritualService = ritualService;
        }

        [HttpPost]
        [Authorize]
        public async Task<ActionResult<RitualDto>> CreateRitual([FromBody] CreateRitualDto ritualDto)
        {
            var createdRitual = await _ritualService.CreateRitualAsync(ritualDto);

            return CreatedAtAction(nameof(GetRitualById), new { id = createdRitual.Id }, createdRitual);
        }

        [HttpPut("{id}")]
        [Authorize]
        public async Task<ActionResult<RitualDto>> UpdateRitual(int id, [FromBody] CreateRitualDto ritualDto)
        {
            var updatedRitual = await _ritualService.UpdateRitualAsync(id, ritualDto);

            if (updatedRitual == null)
            {
                return NotFound();
            }

            return Ok(updatedRitual);
        }

        [HttpGet("{id}")]
        [Authorize]
        public async Task<ActionResult<RitualDto>> GetRitualById(int id)
        {
            var ritual = await _ritualService.GetRitualByIdAsync(id);
            if (ritual == null)
            {
                return NotFound();
            }

            return Ok(ritual);
        }

        [HttpGet]
        [Authorize]
        public async Task<ActionResult<IEnumerable<RitualDto>>> GetAllRituals()
        {
            var rituals = await _ritualService.GetAllRitualsAsync();
            return Ok(rituals);
        }

    }

    public class RitualTypeDto
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class CreateRitualDto
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string FullContent { get; set; } = string.Empty; // For custom uploaded content
        public string ExternalLink { get; set; } = string.Empty; // For external content like YouTube videos
        public decimal TokenAmount { get; set; }
        public RitualTypeEnum RitualType { get; set; } // Use the enum here
        public string MediaUrl { get; set; } = string.Empty;
    }

    public class RitualDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string FullTextContent { get; set; } = string.Empty; // For custom uploaded content
        public string MediaUrl { get; set; } = string.Empty;// For external content like YouTube videos
        public decimal TokenAmount { get; set; }
        public RitualTypeEnum RitualType { get; set; } // Use the enum here
        public bool IsLocked { get; set; }
        public bool IsExternalMediaUrl { get; set; }
        public bool IsProduct { get; set; }
        public double Rating { get; set; }
    }
}
