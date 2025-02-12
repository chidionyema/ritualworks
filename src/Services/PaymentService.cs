using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using haworks.Dto;
using haworks.Contracts;
using haworks.Repositories;
using haworks.Db;
using Microsoft.EntityFrameworkCore.Storage;

namespace haworks.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly ILogger<PaymentService> _logger;
        private readonly SessionService _checkoutSessionService; // Stripe Checkout Session Service
        private readonly IPaymentRepository _paymentRepository;
        private readonly IOrderRepository _orderRepository;

        public PaymentService(
            ILogger<PaymentService> logger,
            SessionService checkoutSessionService, // Inject SessionService
            IPaymentRepository paymentRepository,
            IOrderRepository orderRepository)
        {
            _logger = logger;
            _checkoutSessionService = checkoutSessionService;
            _paymentRepository = paymentRepository;
            _orderRepository = orderRepository;
        }

        public async Task<CreatePaymentIntentResponse> CreatePaymentIntentAsync(CreatePaymentIntentRequest request)
        {
            _logger.LogInformation("CreatePaymentIntentAsync called with OrderId: {OrderId}, UserInfo: {UserInfo}, Items: {@Items}",
                request.OrderId, request.UserInfo, request.Items);

            if (request.Items == null || !request.Items.Any())
            {
                _logger.LogWarning("CreatePaymentIntentAsync: No items to pay for. OrderId: {OrderId}", request.OrderId);
                throw new ArgumentException("No items to pay for.");
            }

            decimal calculatedTotal = request.Items.Sum(x => x.UnitPrice * x.Quantity);
            _logger.LogInformation("Calculated total: {CalculatedTotal} for OrderId: {OrderId}", calculatedTotal, request.OrderId);

            if (request.TotalAmount != calculatedTotal)
            {
                _logger.LogWarning("CreatePaymentIntentAsync: Total amount mismatch. Expected: {CalculatedTotal}, Received: {ReceivedTotal}, OrderId: {OrderId}",
                    calculatedTotal, request.TotalAmount, request.OrderId);
                throw new ArgumentException("Total amount mismatch. Please refresh and try again.");
            }

            const decimal taxRate = 0.1m;
            decimal taxAmount = calculatedTotal * taxRate;
            decimal totalWithTax = calculatedTotal + taxAmount;
            _logger.LogInformation("Tax amount: {TaxAmount}, Total with tax: {TotalWithTax} for OrderId: {OrderId}",
                taxAmount, totalWithTax, request.OrderId);

            var existingPayment = await _paymentRepository.GetPaymentByOrderIdAsync(request.OrderId);
            if (existingPayment != null && existingPayment.Status == PaymentStatus.Pending)
            {
                _logger.LogInformation("Existing pending payment found for OrderId: {OrderId}, StripeSessionId: {StripeSessionId}",
                    request.OrderId, existingPayment.StripeSessionId);
                // Consider retrieving the existing session to check its status
                // and potentially update it instead of creating a new one.
                return new CreatePaymentIntentResponse { SessionId = existingPayment.StripeSessionId };
            }

            try
            {
             
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

                _logger.LogInformation("Creating payment record: {@PaymentRecord}", paymentRecord);
                await _paymentRepository.CreatePaymentAsync(paymentRecord);

                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = request.Items.Select(item => new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = item.Name,
                            },
                            UnitAmount = (long)(item.UnitPrice * 100),
                        },
                        Quantity = item.Quantity,
                    }).ToList(),
                    
                    Mode = "payment",
                    SuccessUrl = "https://api.local.ritualworks.com/api/checkout/success?session_id={CHECKOUT_SESSION_ID}", // Replace with your success URL
                    CancelUrl = "https://api.local.ritualworks.com/api/checkout/cancel", // Replace with your cancel URL
                    Metadata = new Dictionary<string, string>
                    {
                        { "orderId", request.OrderId.ToString() },
                        { "paymentId", paymentRecord.Id.ToString() }
                    }
                };

                _logger.LogInformation("Creating Stripe Checkout Session with options: {@Options}", options);
                var session = await _checkoutSessionService.CreateAsync(options);

                _logger.LogInformation("Stripe Checkout Session created: {SessionId}", session.Id);

                paymentRecord.StripeSessionId = session.Id; // Store Session ID
                // Note: PaymentIntentId is not used for Checkout Sessions
                _logger.LogInformation("Updating payment record with Stripe details: {@PaymentRecord}", paymentRecord);

                await _paymentRepository.UpdatePaymentAsync(paymentRecord);

              
                _logger.LogInformation("Transaction committed for OrderId: {OrderId}", request.OrderId);
                return new CreatePaymentIntentResponse { SessionId = session.Id }; 
            }
            catch (StripeException se)
            {
                _logger.LogError(se, "Stripe error while creating checkout session for OrderId: {OrderId}, Error: {@StripeError}",
                    request.OrderId, se.StripeError);
 
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating checkout session for OrderId: {OrderId}", request.OrderId);
      
                throw;
            }
        }

        public async Task UpdatePaymentStatusAsync(string sessionId)
        {
            _logger.LogInformation("UpdatePaymentStatusAsync called with SessionId: {SessionId}", sessionId);

            // Retrieve the session
            var session = await _checkoutSessionService.GetAsync(sessionId);

            // Extract PaymentId and OrderId from session metadata
            if (!session.Metadata.TryGetValue("paymentId", out var paymentIdString) || !Guid.TryParse(paymentIdString, out var paymentId))
            {
                _logger.LogError("Invalid or missing paymentId in Session metadata: {SessionId}", sessionId);
                throw new ArgumentException("Invalid or missing paymentId in Session metadata.");
            }

            if (!session.Metadata.TryGetValue("orderId", out var orderIdString) || !Guid.TryParse(orderIdString, out var orderId))
            {
                _logger.LogError("Invalid or missing orderId in Session metadata: {SessionId}", sessionId);
                throw new ArgumentException("Invalid or missing orderId in Session metadata.");
            }

            _logger.LogInformation("Extracted PaymentId: {PaymentId}, OrderId: {OrderId} from Session metadata", paymentId, orderId);
            var paymentStatus = GetPaymentStatusFromSession(session);
            IDbContextTransaction transaction = null;
            try
            {
                transaction = await _paymentRepository.BeginTransactionAsync();

                // Update payment record
                var paymentRecord = await _paymentRepository.GetPaymentByIdAsync(paymentId);
                if (paymentRecord == null)
                {
                    _logger.LogError("Payment record not found for PaymentId: {PaymentId}", paymentId);
                    throw new InvalidOperationException($"Payment record not found for PaymentId: {paymentId}");
                }

                _logger.LogInformation("Updating payment record: {@PaymentRecord}", paymentRecord);

                paymentRecord.Status = paymentStatus;
                paymentRecord.IsComplete = paymentStatus == PaymentStatus.Completed;
                await _paymentRepository.UpdatePaymentAsync(paymentRecord);

                // Update order record
                var orderRecord = await _orderRepository.GetOrderByIdAsync(orderId);
                if (orderRecord == null)
                {
                    _logger.LogError("Order record not found for OrderId: {OrderId}", orderId);
                    throw new InvalidOperationException($"Order record not found for OrderId: {orderId}");
                }
                _logger.LogInformation("Updating order record: {@OrderRecord}", orderRecord);

                orderRecord.Status = paymentStatus == PaymentStatus.Completed ? OrderStatus.Completed : OrderStatus.PaymentFailed;
                await _orderRepository.UpdateOrderStatusAsync(orderId, orderRecord.Status);

                await transaction.CommitAsync();
                _logger.LogInformation("Transaction committed for PaymentId: {PaymentId} and OrderId: {OrderId}", paymentId, orderId);

                _logger.LogInformation("Successfully updated PaymentId: {PaymentId} and OrderId: {OrderId} with PaymentStatus: {PaymentStatus} and OrderStatus: {OrderStatus}",
                    paymentId, orderId, paymentStatus, orderRecord.Status);
            }
            catch (StripeException se)
            {
                _logger.LogError(se, "Stripe error while updating PaymentId: {PaymentId} and OrderId: {OrderId}, Error: {@StripeError}",
                    paymentId, orderId, se.StripeError);
                await transaction?.RollbackAsync();
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating PaymentId: {PaymentId} and OrderId: {OrderId}", paymentId, orderId);
                await transaction?.RollbackAsync();
                throw;
            }
        }

        private PaymentStatus GetPaymentStatusFromSession(Session session)
        {
            // Map the Stripe Session status to your PaymentStatus enum
            switch (session.PaymentStatus)
            {
                case "paid":
                    return PaymentStatus.Completed;
                case "unpaid":
                    return PaymentStatus.Pending; // Or another appropriate status
                case "no_payment_required":
                    return PaymentStatus.Completed; // Or another appropriate status
                default:
                    return PaymentStatus.Pending;
            }
        }

    }

    public class PaymentIntentStatusResponse
    {
        public bool IsComplete { get; set; }
        public string Status { get; set; }
        public decimal Amount { get; set; }
        public decimal Tax { get; set; }
    }
}