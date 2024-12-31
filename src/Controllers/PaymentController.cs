using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Stripe;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using haworks.Dto;
using haworks.Db;
using haworks.Contracts;
using haworks.Repositories;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace PaymentService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly ILogger<PaymentController> _logger;
        private readonly PaymentIntentService _paymentIntentService;
        private readonly IPaymentRepository _paymentRepository;
        private readonly string _stripeWebhookSecret;

        public PaymentController(
            ILogger<PaymentController> logger,
            PaymentIntentService paymentIntentService,
            IPaymentRepository paymentRepository,
            IConfiguration config)
        {
            _logger = logger;
            _paymentIntentService = paymentIntentService;
            _paymentRepository = paymentRepository;
            _stripeWebhookSecret = config["Stripe:WebhookSecret"] ?? throw new ArgumentNullException("Stripe Webhook Secret not configured.");
        }

        [HttpPost("create-intent")]
        public async Task<IActionResult> CreateIntent([FromBody] CreatePaymentIntentRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                if (request.Items == null || !request.Items.Any())
                {
                    return BadRequest(new { message = "No items to pay for." });
                }

                // Calculate total amount
                decimal calculatedTotal = request.Items.Sum(x => x.UnitPrice * x.Quantity);
                if (request.TotalAmount != calculatedTotal)
                {
                    _logger.LogWarning("Total amount mismatch: Calculated={CalculatedTotal}, Provided={ProvidedTotal}", calculatedTotal, request.TotalAmount);
                    return BadRequest(new { message = "Total amount mismatch. Please refresh and try again." });
                }

                const decimal taxRate = 0.1m;
                decimal taxAmount = calculatedTotal * taxRate;
                decimal totalWithTax = calculatedTotal + taxAmount;

                // Check for existing PaymentIntent
                var existingPayment = await _paymentRepository.GetPaymentByOrderIdAsync(request.OrderId);
                if (existingPayment?.Status == PaymentStatus.Pending)
                {
                    return Ok(new { clientSecret = existingPayment.StripeSessionId });
                }

                using (var transaction = await _paymentRepository.BeginTransactionAsync())
                {
                    try
                    {
                        // Create payment record
                        var paymentRecord = new Payment
                        {
                            Id = Guid.NewGuid(),
                            OrderDate = DateTime.UtcNow,
                            UserId = request.UserInfo.UserId,
                            OrderId = request.OrderId,
                            Amount = calculatedTotal,
                            Tax = taxAmount,
                            IsComplete = false,
                            Status = PaymentStatus.Pending
                        };

                        await _paymentRepository.CreatePaymentAsync(paymentRecord);

                        var options = new PaymentIntentCreateOptions
                        {
                            Amount = (long)(totalWithTax * 100),
                            Currency = "usd",
                            Metadata = new Dictionary<string, string>
                            {
                                { "orderId", request.OrderId.ToString() },
                                { "paymentId", paymentRecord.Id.ToString() }
                            }
                        };

                        var paymentIntent = await _paymentIntentService.CreateAsync(options);
                        paymentRecord.PaymentIntentId = paymentIntent.Id;
                        await _paymentRepository.UpdatePaymentAsync(paymentRecord);

                        await transaction.CommitAsync();

                        return Ok(new { paymentId = paymentRecord.Id });
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating payment intent.");
                return StatusCode(500, new { message = "Internal server error." });
            }
        }

        [HttpPost("webhook")]
      //  [EnableRateLimiting("WebhookPolicy")]
        public async Task<IActionResult> HandleWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], _stripeWebhookSecret);

                // Enqueue webhook event for asynchronous processing
                await Task.Run(() => ProcessWebhook(stripeEvent));

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook.");
                return BadRequest();
            }
        }

        [HttpGet("{paymentId}/status")]
       // [EnableRateLimiting("StatusPollingPolicy")]
        public async Task<IActionResult> GetPaymentStatus(Guid paymentId)
        {
            try
            {
                var paymentRecord = await _paymentRepository.GetPaymentByIdAsync(paymentId);

                if (paymentRecord == null)
                {
                    return NotFound(new { message = "Payment record not found." });
                }

                return Ok(new
                {
                    isComplete = paymentRecord.IsComplete,
                    status = paymentRecord.Status.ToString(),
                    amount = paymentRecord.Amount,
                    tax = paymentRecord.Tax
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching payment status.");
                return StatusCode(500, new { message = "Internal server error." });
            }
        }

        private async Task ProcessWebhook(Event stripeEvent)
        {
            try
            {
                switch (stripeEvent.Type)
                {
                    case Events.PaymentIntentSucceeded:
                        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
                        if (paymentIntent != null)
                        {
                            await UpdatePaymentRecordAsync(paymentIntent, PaymentStatus.Completed);
                        }
                        break;

                    case Events.PaymentIntentPaymentFailed:
                        var failedPaymentIntent = stripeEvent.Data.Object as PaymentIntent;
                        if (failedPaymentIntent != null)
                        {
                            await UpdatePaymentRecordAsync(failedPaymentIntent, PaymentStatus.Failed);
                        }
                        break;

                    default:
                        _logger.LogInformation("Unhandled event type: {EventType}", stripeEvent.Type);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook event.");
            }
        }

        private async Task UpdatePaymentRecordAsync(PaymentIntent paymentIntent, PaymentStatus status)
        {
            var paymentId = Guid.Parse(paymentIntent.Metadata["paymentId"]);
            var paymentRecord = await _paymentRepository.GetPaymentByIdAsync(paymentId);

            if (paymentRecord != null)
            {
                paymentRecord.Status = status;
                paymentRecord.IsComplete = status == PaymentStatus.Completed;
                await _paymentRepository.UpdatePaymentAsync(paymentRecord);
            }
        }
    }
}
