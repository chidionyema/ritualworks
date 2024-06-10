using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RitualWorks.Contracts;

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
            var (donation, sessionId) = await _donationService.CreateDonationAsync(createDonationDto, Request.Scheme + "://" + Request.Host);
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
    }

    public class CreateDonationDto
    {
        public decimal Amount { get; set; }
        public int? PetitionId { get; set; } // Nullable for direct donations to a ritual
        public int? RitualId { get; set; } // Nullable for donations linked to a petition
        public string UserId { get; internal set; }
    }

    public class DonationDto
    {
        public int Id { get; set; }
        public int? PetitionId { get; set; }
        public int? RitualId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string DonorName { get; set; } = string.Empty;
    }

}
