using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Stripe;
using Stripe.Checkout;
using StripeSubscription = Stripe.Subscription;
using haworks.Db;
using haworks.Dto;
using haworks.Models;
using haworks.Helpers;

namespace haworks.Services
{
     public interface ISubscriptionProcessingService
    {
        // New method to create a checkout session from a SubscriptionRequest.
        Task<Session> CreateCheckoutSessionAsync(SubscriptionRequest request, string userId);

        // Process a completed checkout session (using a Session directly).
        Task ProcessCheckoutSessionAsync(Session session);

        // Process subscription update events.
        Task ProcessSubscriptionUpdatedAsync(Stripe.Event stripeEvent);

        // Retrieve subscription status for a user.
        Task<SubscriptionStatusResponseDto> GetSubscriptionStatusAsync(string userId);
    }
    public class SubscriptionProcessingService : ISubscriptionProcessingService
    {
        private readonly haworksContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IStripeClient _stripeClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SubscriptionProcessingService> _logger;
        private readonly AsyncRetryPolicy<StripeSubscription> _subscriptionRetryPolicy;

        public SubscriptionProcessingService(
            haworksContext context,
            UserManager<User> userManager,
            IStripeClient stripeClient,
            IConfiguration configuration,
            ILogger<SubscriptionProcessingService> logger)
        {
            _context = context;
            _userManager = userManager;
            _stripeClient = stripeClient;
            _configuration = configuration;
            _logger = logger;

            _subscriptionRetryPolicy = Policy<StripeSubscription>
                .Handle<StripeException>()
                .WaitAndRetryAsync(3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(new Random().Next(0, 100)),
                    (outcome, delay, retryCount, ctx) =>
                    {
                        _logger.LogWarning(outcome.Exception, "Retry {RetryCount} for subscription retrieval after {Delay} seconds.", retryCount, delay.TotalSeconds);
                    });
        }

        public async Task<SubscriptionStatusResponseDto> GetSubscriptionStatusAsync(string userId)
        {
            var subscription = await _context.Subscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == SubscriptionStatus.Active);
            return new SubscriptionStatusResponseDto
            {
                IsSubscribed = subscription != null,
                PlanId = subscription?.PlanId,
                ExpiresAt = subscription?.ExpiresAt
            };
        }

        public async Task<Session> CreateCheckoutSessionAsync(SubscriptionRequest request, string userId)
        {
            // Validate that the provided PriceId exists.
            bool isValidPrice = await _context.SubscriptionPlans.AnyAsync(p => p.StripePriceId == request.PriceId);
            if (!isValidPrice)
            {
                _logger.LogWarning("Invalid PriceId '{PriceId}' for user {UserId}.", request.PriceId, userId);
                throw new InvalidOperationException("Invalid subscription plan.");
            }

            // Validate redirect path to avoid open redirects.
            if (!Uri.TryCreate(request.RedirectPath, UriKind.Relative, out _))
            {
                _logger.LogWarning("Invalid redirect path: {RedirectPath}", request.RedirectPath);
                throw new InvalidOperationException("Invalid redirect path.");
            }

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions { Price = request.PriceId, Quantity = 1 }
                },
                Mode = "subscription",
                SuccessUrl = $"{_configuration["Frontend:BaseUrl"]}/subscribe/success?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{_configuration["Frontend:BaseUrl"]}/subscribe/cancel",
                Metadata = new Dictionary<string, string>
                {
                    { "user_id", userId },
                    { "signature", CryptoHelper.ComputeHMACSHA256(_configuration["Stripe:MetadataSignatureSecret"]!, userId) }
                }
            };

            var service = new SessionService(_stripeClient);
            string idempotencyKey = $"{userId}-{DateTime.UtcNow.Ticks}";
            var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };
            return await service.CreateAsync(options, requestOptions);
        }

        // Updated method: now accepts a Session rather than a Stripe.Event.
        public async Task ProcessCheckoutSessionAsync(Session session)
        {
            if (session?.SubscriptionId == null)
            {
                _logger.LogWarning("Checkout session missing SubscriptionId for session {SessionId}.", session?.Id);
                return;
            }

            // Validate metadata signature.
            if (!session.Metadata.TryGetValue("user_id", out string userIdString) ||
                !ValidateMetadataSignature(session.Metadata, _configuration["Stripe:MetadataSignatureSecret"]!))
            {
                _logger.LogError("Metadata signature validation failed for session {SessionId}.", session.Id);
                throw new SecurityException("Invalid metadata signature.");
            }

            try
            {
                // Resolve a Stripe subscription using a dedicated service (or DI, if applicable)
                var subscriptionService = new SubscriptionService(_stripeClient);
                var stripeSubscription = await _subscriptionRetryPolicy.ExecuteAsync(() => subscriptionService.GetAsync(session.SubscriptionId));
                if (stripeSubscription?.Items?.Data?.FirstOrDefault()?.Price == null)
                {
                    _logger.LogError("Missing price data in subscription for session {SessionId}.", session.Id);
                    return;
                }

                var price = stripeSubscription.Items.Data[0].Price;
                var subscriptionPlan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.StripePriceId == price.Id);
                if (subscriptionPlan == null)
                {
                    _logger.LogError("Subscription plan not found for price {PriceId} in session {SessionId}.", price.Id, session.Id);
                    return;
                }

                var existingSubscription = await _context.Subscriptions.FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscription.Id);
                var mappedStatus = MapSubscriptionStatus(stripeSubscription.Status);

                if (existingSubscription == null)
                {
                    var user = await _userManager.FindByIdAsync(userIdString);
                    if (user == null)
                    {
                        _logger.LogError("User not found for user_id {UserId}.", userIdString);
                        return;
                    }
                    var newSubscription = new Subscription
                    {
                        UserId = user.Id,
                        StripeSubscriptionId = stripeSubscription.Id,
                        PlanId = subscriptionPlan.Id,
                        Status = mappedStatus,
                        ExpiresAt = stripeSubscription.CurrentPeriodEnd
                    };
                    _context.Subscriptions.Add(newSubscription);
                    _logger.LogInformation("Created new subscription with StripeSubscriptionId {SubscriptionId}.", stripeSubscription.Id);
                }
                else
                {
                    existingSubscription.Status = mappedStatus;
                    existingSubscription.ExpiresAt = stripeSubscription.CurrentPeriodEnd;
                    _logger.LogInformation("Updated subscription with StripeSubscriptionId {SubscriptionId}.", stripeSubscription.Id);
                }

                await _context.SaveChangesAsync();
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe API error processing subscription for session {SessionId}.", session.Id);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing subscription for session {SessionId}.", session.Id);
                throw;
            }
        }

        public async Task ProcessSubscriptionUpdatedAsync(Stripe.Event stripeEvent)
        {
            var stripeSubscription = stripeEvent.Data.Object as StripeSubscription;
            if (stripeSubscription?.Id == null)
            {
                _logger.LogWarning("Subscription ID missing in event {EventId}.", stripeEvent.Id);
                return;
            }

            try
            {
                var existingSubscription = await _context.Subscriptions.FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscription.Id);
                if (existingSubscription != null)
                {
                    existingSubscription.Status = MapSubscriptionStatus(stripeSubscription.Status);
                    existingSubscription.ExpiresAt = stripeSubscription.CurrentPeriodEnd;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Updated subscription {SubscriptionId}.", stripeSubscription.Id);
                }
                else
                {
                    _logger.LogWarning("Subscription {SubscriptionId} not found in database.", stripeSubscription.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating subscription {SubscriptionId}.", stripeSubscription.Id);
                throw;
            }
        }

        private SubscriptionStatus MapSubscriptionStatus(string stripeStatus) => stripeStatus switch
        {
            "active" => SubscriptionStatus.Active,
            "canceled" => SubscriptionStatus.Canceled,
            "unpaid" => SubscriptionStatus.Expired,
            "past_due" => SubscriptionStatus.Expired,
            "trialing" => SubscriptionStatus.Trialing,
            "incomplete" => SubscriptionStatus.Incomplete,
            _ => SubscriptionStatus.Unknown
        };

        private bool ValidateMetadataSignature(IReadOnlyDictionary<string, string> metadata, string secret)
        {
            if (!metadata.ContainsKey("user_id") || !metadata.ContainsKey("signature"))
                return false;
            string userId = metadata["user_id"];
            string signature = metadata["signature"];
            var expectedSignature = CryptoHelper.ComputeHMACSHA256(secret, userId);
            bool isValid = signature == expectedSignature;
            if (!isValid)
                _logger.LogWarning("Metadata signature mismatch: received {ReceivedSignature}, expected {ExpectedSignature}.", signature, expectedSignature);
            return isValid;
        }
    }
}
