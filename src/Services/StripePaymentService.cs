using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using RitualWorks.Contracts;
using RitualWorks.Settings;
using RitualWorks.Controllers;

namespace RitualWorks.Services
{
    public class StripePaymentService : IPaymentService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<StripePaymentService> _logger;
        private readonly StripeSettings _stripeSettings;

        public StripePaymentService(IConfiguration configuration, ILogger<StripePaymentService> logger, IOptions<StripeSettings> stripeSettings)
        {
            _configuration = configuration;
            _logger = logger;
            _stripeSettings = stripeSettings.Value;
            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;
        }

        public async Task<string> CreateCheckoutSessionAsync(string userId, List<CheckoutItem> items)
        {
            try
            {
                var domain = _configuration["Domain"];

                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions>(),
                    Mode = "payment",
                    SuccessUrl = $"{domain}/checkout/success?session_id={{CHECKOUT_SESSION_ID}}",
                    CancelUrl = $"{domain}/checkout/cancel",
                    ClientReferenceId = userId
                };

                foreach (var item in items)
                {
                    var sessionLineItem = new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)(item.Price * 100),
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = item.Name,
                            },
                        },
                        Quantity = item.Quantity,
                    };
                    options.LineItems.Add(sessionLineItem);
                }

                var service = new SessionService();
                Session session = await service.CreateAsync(options);

                return session.Id;
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error occurred during checkout session creation.");
                throw new Exception("An error occurred with the payment provider. Please try again.");
            }
        }
    }
}
