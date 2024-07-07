using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RitualWorks.Services;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace RitualWorks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CheckoutController : ControllerBase
    {
        private readonly CheckoutService _checkoutService;
        private readonly ILogger<CheckoutController> _logger;

        public CheckoutController(CheckoutService checkoutService, ILogger<CheckoutController> logger)
        {
            _checkoutService = checkoutService;
            _logger = logger;
        }

        [HttpPost("create-session")]
        [Authorize]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] List<CheckoutItem> items)
        {
            if (items == null || !items.Any())
            {
                return BadRequest("No items in the checkout.");
            }

            try
            {
                var userId = GetUserId();
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User is not logged in.");
                }

                var sessionId = await _checkoutService.CreateCheckoutSessionAsync(items, userId);
                return Ok(new { id = sessionId });
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, ex.Message);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing your request.");
                return StatusCode(500, "An error occurred while processing your request.");
            }
        }

        private string GetUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }

    public class CheckoutItem
    {
        public Guid ProductId { get; set; }
        public long Quantity { get; set; }
        public decimal Price { get; set; }
        public string Name { get; set; }
    }
}
