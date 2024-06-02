using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using RitualWorks.DTOs;
using RitualWorks.Services;
using Microsoft.AspNetCore.Http;
using RitualWorks.Db;

namespace RitualWorks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RitualsController : ControllerBase
    {
        private readonly IRitualService _ritualService;

        public RitualsController(IRitualService ritualService)
        {
            _ritualService = ritualService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateRitual([FromForm] CreateRitualDto ritualDto, IFormFile mediaFile)
        {
            if (string.IsNullOrEmpty(ritualDto.FullContent) && mediaFile == null && string.IsNullOrEmpty(ritualDto.ExternalLink))
            {
                return BadRequest("Either media file, full content, or external link must be provided.");
            }

            Stream mediaStream = null;
            if (mediaFile != null)
            {
                mediaStream = mediaFile.OpenReadStream();
            }

            var result = await _ritualService.CreateRitualAsync(ritualDto, mediaStream);
            return Ok(result);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRitual(int id, [FromForm] CreateRitualDto ritualDto, IFormFile? mediaFile)
        {
            Stream mediaStream = null;
            if (mediaFile != null)
            {
                mediaStream = mediaFile.OpenReadStream();
            }

            var ritual = await _ritualService.UpdateRitualAsync(id, ritualDto, mediaStream);
            if (ritual == null)
            {
                return NotFound();
            }
            return Ok(ritual);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetRitualById(int id)
        {
            var ritual = await _ritualService.GetRitualByIdAsync(id);
            if (ritual == null)
            {
                return NotFound();
            }
            return Ok(ritual);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllRituals()
        {
            var rituals = await _ritualService.GetAllRitualsAsync();
            return Ok(rituals);
        }

        [HttpPost("lock/{id}")]
        public async Task<IActionResult> LockRitual(int id)
        {
            var success = await _ritualService.LockRitualAsync(id);
            if (!success)
            {
                return NotFound();
            }
            return NoContent();
        }

        [HttpPost("rate/{id}")]
        public async Task<IActionResult> RateRitual(int id, [FromBody] double rating)
        {
            var success = await _ritualService.RateRitualAsync(id, rating);
            if (!success)
            {
                return NotFound();
            }
            return Ok();
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchRituals([FromQuery] string query, [FromQuery] RitualTypeEnum? type)
        {
            var rituals = await _ritualService.SearchRitualsAsync(query, type);
            return Ok(rituals);
        }
    }
}
