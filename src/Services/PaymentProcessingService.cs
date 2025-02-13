using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using haworks.Db;
using haworks.Dto;
using haworks.Repositories;
using haworks.Contracts;

namespace haworks.Services
{
    public interface IPaymentProcessingService
    {
        Task<(Order order, Session stripeSession)> ProcessCheckoutAsync(StartCheckoutRequest request, string userId);
        Task HandlePaymentSessionAsync(Session session);
    }

    public class PaymentProcessingService : IPaymentProcessingService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IProductRepository _productRepository;
        private readonly IPaymentRepository _paymentRepository;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PaymentProcessingService> _logger;
        private readonly ITelemetryService _telemetry;
        private readonly IDataProtector _protector;

        public PaymentProcessingService(
            IOrderRepository orderRepository,
            IProductRepository productRepository,
            IPaymentRepository paymentRepository,
            IConfiguration configuration,
            ILogger<PaymentProcessingService> logger,
            ITelemetryService telemetry,
            IDataProtectionProvider protectionProvider)
        {
            _orderRepository = orderRepository;
            _productRepository = productRepository;
            _paymentRepository = paymentRepository;
            _configuration = configuration;
            _logger = logger;
            _telemetry = telemetry;
            _protector = protectionProvider.CreateProtector("Checkout.OrderId");
        }

        public async Task<(Order order, Session stripeSession)> ProcessCheckoutAsync(StartCheckoutRequest request, string userId)
        {
            // Generate idempotency key with a time component.
            var idempotencyKey = GenerateIdempotencyKey(userId, request);

            await using var transaction = await _orderRepository.BeginTransactionAsync();
            if (await _orderRepository.GetOrderByIdempotencyKeyAsync(idempotencyKey) is { } duplicate)
            {
                _logger.LogInformation("Duplicate order detected with idempotency key: {Key}", idempotencyKey);
                throw new InvalidOperationException("Duplicate order detected.");
            }

            // Validate stock for all items in one query (optimized).
            var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _productRepository.GetProductsByIdsAsync(productIds);
            foreach (var item in request.Items)
            {
                var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (product == null || !await _productRepository.ValidateStockAsync(item.ProductId, item.Quantity))
                    throw new InvalidOperationException($"Insufficient stock for product {item.ProductId}");
            }

            var totalAmount = request.Items.Sum(i => products.First(p => p.Id == i.ProductId).UnitPrice * i.Quantity);
            var tax = totalAmount * _configuration.GetValue<decimal>("TaxRate");

            var order = new Order(
                id: Guid.NewGuid(),
                totalAmount: totalAmount + tax,
                status: OrderStatus.Pending,
                userId: userId)
            {
                IdempotencyKey = idempotencyKey,
                OrderItems = request.Items.Select(i => new OrderItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = products.First(p => p.Id == i.ProductId).UnitPrice
                }).ToList()
            };

            await _orderRepository.CreateOrderAsync(order);

            var paymentRecord = new Payment
            {
                Id = Guid.NewGuid(),
                OrderDate = DateTime.UtcNow,
                UserId = userId,
                OrderId = order.Id,
                Amount = totalAmount,
                Tax = tax,
                IsComplete = false,
                Status = PaymentStatus.Pending,
                StripeSessionId = null
            };

            await _paymentRepository.CreatePaymentAsync(paymentRecord);
            await transaction.CommitAsync();

            var stripeClient = new StripeClient(_configuration["Stripe:SecretKey"]);
            var sessionService = new SessionService(stripeClient);
            var sessionOptions = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = request.Items.Select(item =>
                {
                    var product = products.First(p => p.Id == item.ProductId);
                    return new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = product.Name },
                            UnitAmount = (long)Math.Round(product.UnitPrice * 100, MidpointRounding.AwayFromZero)
                        },
                        Quantity = item.Quantity
                    };
                }).ToList(),
                Mode = "payment",
                SuccessUrl = $"{_configuration["Frontend:BaseUrl"]}/checkout/success?token={GenerateSuccessToken(order.Id)}",
                CancelUrl = $"{_configuration["Frontend:BaseUrl"]}/checkout/cancel"
            };

            var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };
            var stripeSession = await sessionService.CreateAsync(sessionOptions, requestOptions);

            await using var updateTransaction = await _paymentRepository.BeginTransactionAsync();
            paymentRecord.StripeSessionId = stripeSession.Id;
            await _paymentRepository.UpdatePaymentAsync(paymentRecord);
            await updateTransaction.CommitAsync();

            _telemetry.TrackEvent("StripeSessionCreated", new Dictionary<string, string>
            {
                ["OrderId"] = order.Id.ToString(),
                ["Amount"] = paymentRecord.Amount.ToString("C")
            });

            return (order, stripeSession);
        }

        public async Task HandlePaymentSessionAsync(Session session)
        {
            var paymentRecord = await _paymentRepository.GetPaymentByStripeSessionIdAsync(session.Id);
            if (paymentRecord == null)
            {
                _logger.LogWarning("Payment record not found for session {SessionId}", session.Id);
                throw new InvalidOperationException($"Payment record not found for session {session.Id}");
            }

            await using var transaction = await _paymentRepository.BeginTransactionAsync();
            paymentRecord.Status = PaymentStatus.Completed;
            paymentRecord.IsComplete = true;
            await _paymentRepository.UpdatePaymentAsync(paymentRecord);

            var order = await _orderRepository.GetOrderByIdAsync(paymentRecord.OrderId);
            if (order == null)
                throw new InvalidOperationException($"Order not found for ID {paymentRecord.OrderId}");
            order.Status = OrderStatus.Completed;
            await _orderRepository.UpdateOrderStatusAsync(order.Id, order.Status);

            foreach (var item in order.OrderItems)
            {
                bool updated = await _productRepository.DecrementStockAsync(item.ProductId, item.Quantity);
                if (!updated)
                    throw new InvalidOperationException($"Insufficient stock for product {item.ProductId}");
            }

            await transaction.CommitAsync();
            _logger.LogInformation("Successfully processed payment session {SessionId}", session.Id);
        }

        private string GenerateIdempotencyKey(string userId, StartCheckoutRequest request)
        {
            var payload = new
            {
                UserId = userId,
                Items = request.Items.OrderBy(i => i.ProductId).Select(i => new { i.ProductId, i.Quantity }),
                Timestamp = DateTime.UtcNow.Ticks // Ensures uniqueness across time.
            };

            using var sha256 = SHA256.Create();
            return Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
        }

        private string GenerateSuccessToken(Guid orderId) => _protector.Protect(orderId.ToString());
    }
}
