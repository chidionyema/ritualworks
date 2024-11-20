using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using System.IO;
using System.Threading.Tasks;
using haworks.Contracts;
using haworks.Settings;
using haworks.Db;
using System;
using Microsoft.AspNetCore.Authorization;
using haworks.Models;
namespace haworks.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WebhookController : ControllerBase
    {
        private readonly IOrderRepository _orderRepository;
        private readonly ILogger<WebhookController> _logger;
        private readonly StripeSettings _stripeSettings;

        public WebhookController(IOrderRepository orderRepository, ILogger<WebhookController> logger, IOptions<StripeSettings> stripeSettings)
        {
            _orderRepository = orderRepository;
            _logger = logger;
            _stripeSettings = stripeSettings.Value;
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Handle()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], _stripeSettings.WebhookSecret);

                if (stripeEvent.Type == Events.CheckoutSessionCompleted)
                {
                    var session = stripeEvent.Data.Object as Session;
                    await HandleCheckoutSessionCompleted(session);
                }

                return Ok();
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe webhook error occurred.");
                return BadRequest();
            }
        }

        private async Task HandleCheckoutSessionCompleted(Session session)
        {
            var userId = session.ClientReferenceId;
            var orderId = session.Id;

            var order = await _orderRepository.GetOrderByIdAsync(Guid.Parse(orderId));

            if (order != null)
            {
                await _orderRepository.UpdateOrderStatusAsync(order.Id, OrderStatus.Completed);
            }
        }
    }
}
