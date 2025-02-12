using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using haworks.Db;
using haworks.Dto;
using haworks.Models; // Ensure models like Subscription, SubscriptionPlan, etc., are here.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.Filters;
using Polly;
using Polly.Retry;
using Stripe;
using Stripe.Checkout;
using StripeSubscription = Stripe.Subscription;

namespace haworks.Controllers
{
    /// <summary>
    /// API controller for handling subscription related operations.
    /// Manages user subscriptions via Stripe, including checkout session creation,
    /// webhook event processing, and subscription status retrieval.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("WebhookPolicy")]
    public class SubscriptionController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly haworksContext _context;
        private readonly IStripeClient _stripeClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SubscriptionController> _logger;
        private readonly AsyncRetryPolicy<Session> _checkoutSessionRetryPolicy;
        private readonly AsyncRetryPolicy<StripeSubscription> _subscriptionRetryPolicy;
        private readonly AsyncRetryPolicy<PingResponse> _stripeHealthCheckRetryPolicy;

        public SubscriptionController(
            UserManager<User> userManager,
            haworksContext context,
            IStripeClient stripeClient,
            IConfiguration configuration,
            ILogger<SubscriptionController> logger)
        {
            _userManager = userManager;
            _context = context;
            _stripeClient = stripeClient;
            _configuration = configuration;
            _logger = logger;

            // Validate essential configuration values on startup.
            if (string.IsNullOrEmpty(_configuration["Stripe:WebhookSecret"]))
                throw new InvalidOperationException("Missing Stripe:WebhookSecret configuration. This is essential for webhook security.");
            if (string.IsNullOrEmpty(_configuration["Frontend:BaseUrl"]))
                throw new InvalidOperationException("Missing Frontend:BaseUrl configuration. This is required for constructing redirect URLs.");
            if (string.IsNullOrEmpty(_configuration["Stripe:MetadataSignatureSecret"]))
                throw new InvalidOperationException("Missing Stripe:MetadataSignatureSecret configuration. This is crucial for metadata security.");
            if (string.IsNullOrEmpty(_configuration["Stripe:AccountId"]))
                throw new InvalidOperationException("Missing Stripe:AccountId configuration. This is required for the Stripe health check.");

            // Define Polly retry policies with exponential backoff and jitter.
            _checkoutSessionRetryPolicy = Policy<Session>
                .Handle<StripeException>()
                .WaitAndRetryAsync(3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(new Random().Next(0, 100)),
                    (outcome, delay, retryCount, context) =>
                    {
                        _logger.LogWarning(outcome.Exception, "Polly retry {RetryCount} for Checkout Session creation after {Delay} seconds due to: {ExceptionType}.",
                            retryCount, delay.TotalSeconds, outcome.Exception?.GetType().Name);
                    });

            _subscriptionRetryPolicy = Policy<StripeSubscription>
               .Handle<StripeException>()
               .WaitAndRetryAsync(3,
                   retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(new Random().Next(0, 100)),
                   (outcome, delay, retryCount, context) =>
                   {
                       _logger.LogWarning(outcome.Exception, "Polly retry {RetryCount} for Subscription retrieval after {Delay} seconds due to: {ExceptionType}.",
                           retryCount, delay.TotalSeconds, outcome.Exception?.GetType().Name);
                   });

            _stripeHealthCheckRetryPolicy = Policy<PingResponse>
                .Handle<StripeException>()
                .WaitAndRetryAsync(3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(new Random().Next(0, 100)),
                    (outcome, delay, retryCount, context) =>
                    {
                        _logger.LogWarning(outcome.Exception, "Polly retry {RetryCount} for Stripe Health Check...", retryCount);
                    });
        } // End of constructor

        [HttpGet("status")]
        public async Task<IActionResult> GetSubscriptionStatus()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                _logger.LogWarning("GetSubscriptionStatus: Unauthorized access. User context is null.");
                return Unauthorized();
            }

            // IMPORTANT: Ensure a database index exists on Subscriptions (UserId and Status).
            var subscription = await _context.Subscriptions
                .FirstOrDefaultAsync(s => s.UserId == user.Id && s.Status == SubscriptionStatus.Active);

            return Ok(new SubscriptionStatusResponseDto
            {
                IsSubscribed = subscription != null,
                PlanId = subscription?.PlanId,
                ExpiresAt = subscription?.ExpiresAt
            });
        }

        [HttpPost("create-checkout-session")]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] SubscriptionRequest request)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("CreateCheckoutSession: Model validation failed. Errors: {Errors}", ModelState);
                return BadRequest(ModelState);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            // Validate that the provided PriceId exists in our SubscriptionPlans.
            var isValidPrice = await _context.SubscriptionPlans.AnyAsync(p => p.StripePriceId == request.PriceId);
            if (!isValidPrice)
            {
                _logger.LogWarning($"CreateCheckoutSession: Invalid PriceId '{request.PriceId}' for user {user.Id}.");
                return BadRequest("Invalid subscription plan.");
            }

             // Validate redirectPath to prevent open redirects
            if (!Uri.TryCreate(request.RedirectPath, UriKind.Relative, out _))
            {
                _logger.LogWarning("Invalid redirect path: {RedirectPath}", request.RedirectPath);
                return BadRequest("Invalid redirect path");
            }


            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new() { Price = request.PriceId, Quantity = 1 }
                },
                Mode = "subscription",
                SuccessUrl = $"{_configuration["Frontend:BaseUrl"]}/subscribe/success?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{_configuration["Frontend:BaseUrl"]}/subscribe/cancel",
                Metadata = new Dictionary<string, string>
                {
                    { "user_id", user.Id.ToString() },
                    { "signature", ComputeMetadataSignature(user.Id.ToString(), _configuration["Stripe:MetadataSignatureSecret"]) }
                }
            };

            try
            {
                var service = new SessionService(_stripeClient);
                var session = await _checkoutSessionRetryPolicy.ExecuteAsync(async () => await service.CreateAsync(options));

                _logger.LogInformation("Checkout session created for user {UserId}. SessionId: {SessionId}, PriceId: {PriceId}.", user.Id, session.Id, request.PriceId);
                return Ok(new CreateCheckoutSessionResponseDto { SessionId = session.Id });
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error creating checkout session for user {UserId}. PriceId: {PriceId}.", user.Id, request.PriceId);
                return StatusCode(StatusCodes.Status500InternalServerError, "Payment error occurred.");
            }
        }

        [HttpPost("webhook")]
        [AllowAnonymous]
        [ServiceFilter(typeof(ValidateStripeWebhookAttribute))]
        public async Task<IActionResult> HandleWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            string stripeSignature = Request.Headers["Stripe-Signature"];
            Event stripeEvent;

            try
            {
                stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, _configuration["Stripe:WebhookSecret"]);

                // Ignore events older than 5 minutes.
                if ((DateTime.UtcNow - stripeEvent.Created).TotalMinutes > 5)
                {
                    _logger.LogWarning("HandleWebhook: Ignored old event. EventId: {EventId}", stripeEvent.Id);
                    return Ok("Old event ignored.");
                }
            }
            catch (StripeException e)
            {
                _logger.LogError(e, "HandleWebhook: Stripe signature verification failed.");
                return BadRequest("Invalid signature.");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (await _context.WebhookEvents.AnyAsync(e => e.StripeEventId == stripeEvent.Id))
                {
                    _logger.LogInformation("HandleWebhook: Event already processed. EventId: {EventId}", stripeEvent.Id);
                    return Ok("Webhook event already processed.");
                }

                if (stripeEvent.Type == "checkout.session.completed")
                {
                    await HandleCheckoutSession(stripeEvent);
                }
                else if (stripeEvent.Type == "customer.subscription.updated" ||
                         stripeEvent.Type == "customer.subscription.deleted" ||
                         stripeEvent.Type == "customer.subscription.canceled")
                {
                    await HandleSubscriptionUpdated(stripeEvent);
                }

                // Ensure your WebhookEvent model has a property named EventJson.
                _context.WebhookEvents.Add(new WebhookEvent
                {
                    StripeEventId = stripeEvent.Id,
                    ProcessedAt = DateTime.UtcNow,
                    EventType = stripeEvent.Type,
                    EventJson = json
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("HandleWebhook: Processed event. EventId: {EventId}", stripeEvent.Id);
                return Ok("Webhook received and processed.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "HandleWebhook: Error processing event. EventId: {EventId}", stripeEvent.Id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Webhook processing failed.");
            }
        }

        private async Task HandleCheckoutSession(Event stripeEvent)
        {
            var session = stripeEvent.Data.Object as Session;
            if (session?.SubscriptionId == null)
            {
                _logger.LogWarning("HandleCheckoutSession: Session missing SubscriptionId. EventId: {EventId}", stripeEvent.Id);
                return;
            }

            string userIdString = session.Metadata["user_id"];
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out Guid userId))
            {
                _logger.LogError("HandleCheckoutSession: Invalid/missing user_id in metadata. SessionId: {SessionId}", session.Id);
                throw new InvalidOperationException($"Invalid user ID '{userIdString}' in session metadata. SessionId: {session.Id}");
            }

            if (!ValidateMetadataSignature(session.Metadata, _configuration["Stripe:MetadataSignatureSecret"]))
            {
                _logger.LogError("HandleCheckoutSession: Metadata signature validation failed for SessionId: {SessionId}", session.Id);
                throw new SecurityException("Invalid metadata signature. Session tampered or invalid.");
            }

            try
            {
                var subscriptionService = new SubscriptionService(_stripeClient);
                var stripeSubscription = await _subscriptionRetryPolicy.ExecuteAsync(async () => await subscriptionService.GetAsync(session.SubscriptionId));

                if (stripeSubscription?.Items?.Data?.FirstOrDefault()?.Price == null)
                {
                    _logger.LogError("HandleCheckoutSession: Subscription data invalid - missing Price. SessionId: {SessionId}", session.Id);
                    return;
                }
                var price = stripeSubscription.Items.Data[0].Price;

                var subscriptionPlan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.StripePriceId == price.Id);
                if (subscriptionPlan == null)
                {
                    _logger.LogError("HandleCheckoutSession: Subscription plan not found in DB for Stripe PriceId: {PriceId}. SessionId: {SessionId}", price.Id, session.Id);
                    return;
                }

                var existingSubscription = await _context.Subscriptions.FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscription.Id);
                var subscriptionStatus = MapSubscriptionStatus(stripeSubscription.Status);

                if (existingSubscription == null)
                {
                    var user = await _userManager.FindByIdAsync(userIdString);
                    if (user == null)
                    {
                        _logger.LogError("HandleCheckoutSession: User not found for UserId: {UserId}", userId);
                        return;
                    }

                    var newSubscription = new Subscription
                    {
                        UserId = userId.ToString(),
                        StripeSubscriptionId = stripeSubscription.Id,
                        PlanId = subscriptionPlan.Id,
                        Status = subscriptionStatus,
                        ExpiresAt = stripeSubscription.CurrentPeriodEnd
                    };
                    _context.Subscriptions.Add(newSubscription);
                    _logger.LogInformation("HandleCheckoutSession: Created new subscription. StripeSubscriptionId: {StripeSubscriptionId}", stripeSubscription.Id);
                }
                else
                {
                    existingSubscription.Status = subscriptionStatus;
                    existingSubscription.ExpiresAt = stripeSubscription.CurrentPeriodEnd;
                    _logger.LogInformation("HandleCheckoutSession: Updated subscription. StripeSubscriptionId: {StripeSubscriptionId}", stripeSubscription.Id);
                }
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "HandleCheckoutSession: Stripe API error processing subscription. SessionId: {SessionId}", session.Id);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleCheckoutSession: Error processing subscription. SessionId: {SessionId}", session.Id);
                throw;
            }
        }

        private async Task HandleSubscriptionUpdated(Event stripeEvent)
        {
            var stripeSubscription = stripeEvent.Data.Object as StripeSubscription;
            if (stripeSubscription?.Id == null)
            {
                _logger.LogWarning("HandleSubscriptionUpdated: Event missing Subscription ID. EventId: {EventId}", stripeEvent.Id);
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
                    _logger.LogInformation("HandleSubscriptionUpdated: Updated subscription. StripeSubscriptionId: {StripeSubscriptionId}", stripeSubscription.Id);
                }
                else
                {
                    _logger.LogWarning("HandleSubscriptionUpdated: Subscription not found in DB for StripeSubscriptionId: {StripeSubscriptionId}", stripeSubscription.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleSubscriptionUpdated: Error updating subscription. StripeSubscriptionId: {StripeSubscriptionId}", stripeSubscription.Id);
                throw;
            }
        }

        private SubscriptionStatus MapSubscriptionStatus(string stripeStatus)
        {
            return stripeStatus switch
            {
                "active" => SubscriptionStatus.Active,
                "canceled" => SubscriptionStatus.Canceled,
                "unpaid" => SubscriptionStatus.Expired,
                "past_due" => SubscriptionStatus.Expired,
                "trialing" => SubscriptionStatus.Trialing,
                "incomplete" => SubscriptionStatus.Incomplete,
                "incomplete_expired" => SubscriptionStatus.Expired,
                _ => SubscriptionStatus.Unknown
            };
        }

        private bool ValidateMetadataSignature(IReadOnlyDictionary<string, string> metadata, string secret)
        {
            if (!metadata.ContainsKey("user_id") || !metadata.ContainsKey("signature"))
                return false;

            string userId = metadata["user_id"];
            string signature = metadata["signature"];
            if (string.IsNullOrEmpty(secret))
            {
                _logger.LogError("Metadata signature validation secret is not configured.");
                return false;
            }

            var expectedSignature = ComputeHMACSHA256(secret, userId);
            bool isValid = signature == expectedSignature;
            if (!isValid)
            {
                _logger.LogWarning("Metadata signature validation failed. Received: {ReceivedSignature}, Expected: {ExpectedSignature}", signature, expectedSignature);
            }
            return isValid;
        }

        private string ComputeHMACSHA256(string secretKey, string data)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey)))
            {
                byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }

        private string ComputeMetadataSignature(string userId, string secret)
        {
            return ComputeHMACSHA256(secret, userId);
        }

        [HttpGet("health")]
        [AllowAnonymous]
        public async Task<IActionResult> HealthCheck()
        {
            bool dbHealthy;
            try
            {
                dbHealthy = await _context.Database.CanConnectAsync();
            }
            catch
            {
                dbHealthy = false;
            }

            bool stripeHealthy = false;
            try
            {
                var pingService = new PingService(_stripeClient, _configuration);
                var pingResult = await _stripeHealthCheckRetryPolicy.ExecuteAsync(async () => await pingService.PingAsync());
                stripeHealthy = pingResult.Object?.ToString() == "ok";
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe Health Check failed with exception.");
                stripeHealthy = false;
            }

            bool isHealthy = dbHealthy && stripeHealthy;
            _logger.LogInformation("Health Check - Database: {DbHealth}, Stripe API: {StripeHealth}, Overall: {OverallHealth}",
                dbHealthy ? "Healthy" : "Unhealthy",
                stripeHealthy ? "Healthy" : "Unhealthy",
                isHealthy ? "Healthy" : "Unhealthy");

            return isHealthy ? Ok("Healthy") : StatusCode(StatusCodes.Status503ServiceUnavailable, "Service unavailable");
        }

        // A minimal PingService implementation to perform a simple Stripe API call.
        private class PingService
        {
            private readonly IStripeClient _client;
            private readonly IConfiguration _configuration;
            public PingService(IStripeClient client, IConfiguration configuration)
            {
                _client = client;
                _configuration = configuration;
            }
            public async Task<PingResponse> PingAsync()
            {
                var service = new AccountService(_client);
                var accountId = _configuration["Stripe:AccountId"];
                // Pass required parameters (here we pass only the account id).
                var account = await service.GetAsync(accountId);
                return new PingResponse { Object = "ok" };
            }
        }

        private class PingResponse
        {
            public string Object { get; set; } = string.Empty;
        }
    }

    // DTOs
    public class SubscriptionRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string PriceId { get; set; }
        public string RedirectPath { get; set; }
        
    }

    public class SubscriptionStatusResponseDto
    {
        public bool IsSubscribed { get; set; }
        public string? PlanId { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    public class CreateCheckoutSessionResponseDto
    {
        public string SessionId { get; set; } = string.Empty;
    }

     public class ValidateStripeWebhookAttribute : ActionFilterAttribute
     {
            private readonly IConfiguration _configuration;

            public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                var request = context.HttpContext.Request;
                var json = await new StreamReader(request.Body).ReadToEndAsync();
                request.Body.Position = 0; // Reset for model binding
                
                try
                {
                    var stripeEvent = EventUtility.ConstructEvent(
                        json,
                        request.Headers["Stripe-Signature"],
                        _configuration["Stripe:WebhookSecret"]
                    );
                    
                    // Replay attack protection
                    if (DateTime.UtcNow - stripeEvent.Created > TimeSpan.FromMinutes(5))
                    {
                        context.Result = new BadRequestObjectResult("Event too old");
                        return;
                    }
                    
                    context.HttpContext.Items["StripeEvent"] = stripeEvent;
                    await next();
                }
                catch (StripeException)
                {
                    context.Result = new BadRequestObjectResult("Invalid signature");
                    return;
                }
            } 
            
      }
}
