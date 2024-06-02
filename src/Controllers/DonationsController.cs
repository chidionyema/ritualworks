using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RitualWorks.Contracts;
using RitualWorks.DTOs;

namespace RitualWorks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DonationsController : ControllerBase
    {
        private readonly IDonationService _donationService;

        public DonationsController(IDonationService donationService)
        {
            _donationService = donationService;
        }

        [HttpPost]
        public async Task<IActionResult> CreateDonation([FromBody] CreateDonationDto createDonationDto)
        {
            var donation = await _donationService.CreateDonationAsync(createDonationDto);
            return CreatedAtAction(nameof(GetDonationById), new { id = donation.Id }, donation);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetDonationById(int id)
        {
            var donation = await _donationService.GetDonationByIdAsync(id);
            if (donation == null)
            {
                return NotFound();
            }
            return Ok(donation);
        }

        [HttpGet("by-ritual/{ritualId}")]
        public async Task<IActionResult> GetDonationsByRitualId(int ritualId)
        {
            var donations = await _donationService.GetDonationsByRitualIdAsync(ritualId);
            return Ok(donations);
        }
    }
}
