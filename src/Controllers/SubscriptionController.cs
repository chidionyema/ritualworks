using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using haworks.Dto;
using haworks.Services;
using Stripe.Checkout;

namespace haworks.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SubscriptionController : ControllerBase
    {
        private readonly ISubscriptionProcessingService _subscriptionService;
        private readonly ILogger<SubscriptionController> _logger;

        public SubscriptionController(ISubscriptionProcessingService subscriptionService, ILogger<SubscriptionController> logger)
        {
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetSubscriptionStatus()
        {
            string userId = User?.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            try
            {
                var status = await _subscriptionService.GetSubscriptionStatusAsync(userId);
                return Ok(status);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve subscription status for user {UserId}.", userId);
                return StatusCode(500, new { message = "Failed to retrieve subscription status." });
            }
        }

        [HttpPost("create-checkout-session")]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] SubscriptionRequest request)
        {
            string userId = User?.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            try
            {
                var stripeSession = await _subscriptionService.CreateCheckoutSessionAsync(request, userId);
                return Ok(new CreateCheckoutSessionResponseDto { SessionId = stripeSession.Id });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Failed to create subscription checkout session for user {UserId}.", userId);
                return StatusCode(500, new { message = "Failed to create checkout session." });
            }
        }
    }
}
