using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RitualWorks.Contracts;
using RitualWorks.DTOs;
using RitualWorks.Services;

namespace RitualWorks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PetitionsController : ControllerBase
    {
        private readonly IPetitionService _petitionService;

        public PetitionsController(IPetitionService petitionService)
        {
            _petitionService = petitionService;
        }

        [HttpPost]
        public async Task<ActionResult<PetitionDto>> CreatePetition(CreatePetitionDto createPetitionDto)
        {
            var petition = await _petitionService.CreatePetitionAsync(createPetitionDto);
            return CreatedAtAction(nameof(GetPetitionById), new { id = petition.Id }, petition);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<PetitionDto>> GetPetitionById(int id)
        {
            var petition = await _petitionService.GetPetitionByIdAsync(id);
            if (petition == null)
            {
                return NotFound();
            }
            return Ok(petition);
        }

        [HttpGet("by-ritual/{ritualId}")]
        public async Task<ActionResult<IEnumerable<PetitionDto>>> GetPetitionsByRitualId(int ritualId)
        {
            var petitions = await _petitionService.GetPetitionsByRitualIdAsync(ritualId);
            return Ok(petitions);
        }
    }
}
