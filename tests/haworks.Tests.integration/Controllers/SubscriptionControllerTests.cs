#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Stripe;
using Haworks.Tests;
using haworks.Dto;
using Haworks.Infrastructure.Repositories;
using haworks.Helpers;
using haworks.Db;
using haworks.Models;
using Haworks.Infrastructure.Data;

namespace Haworks.Tests.IntegrationTests.Controllers
{
    [Collection("Integration Tests")]
    public class SubscriptionControllerTests : IAsyncLifetime
    {
        private readonly HttpClient _client;
        private readonly IntegrationTestFixture _fixture;
        private readonly string _testUserId = "test_user_123";
        private readonly string _validPriceId = "price_valid_123";
        private readonly string _invalidPriceId = "price_invalid_999";

        public SubscriptionControllerTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateAuthorizedClient(_testUserId);
        }

        public async Task InitializeAsync()
        {
            await _fixture.ResetDatabaseAsync();
            await SeedTestDataAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task GetSubscriptionStatus_NoSubscription_ReturnsFalse()
        {
            // Act
            var response = await _client.GetAsync("/api/subscription/status");
            var content = await response.Content.ReadFromJsonAsync<SubscriptionStatusResponseDto>();

            // Assert
            response.EnsureSuccessStatusCode();
            Assert.False(content?.IsSubscribed);
        }

        [Fact]
        public async Task CreateCheckoutSession_ValidRequest_ReturnsSessionId()
        {
            // Arrange
            var request = new SubscriptionRequest { PriceId = _validPriceId };

            // Act
            var response = await _client.PostAsJsonAsync("/api/subscription/create-checkout-session", request);
            var content = await response.Content.ReadFromJsonAsync<CreateCheckoutSessionResponseDto>();

            // Assert
            response.EnsureSuccessStatusCode();
            Assert.NotNull(content?.SessionId);
            
            // Verify database state
            using var scope = _fixture.Factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IOrderContextRepository>();
            var payment = await repo.GetPaymentByStripeSessionIdAsync(content.SessionId);
            Assert.NotNull(payment);
        }

        [Fact]
        public async Task CreateCheckoutSession_InvalidPriceId_ReturnsBadRequest()
        {
            // Arrange
            var request = new SubscriptionRequest { PriceId = _invalidPriceId };

            // Act
            var response = await _client.PostAsJsonAsync("/api/subscription/create-checkout-session", request);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        private async Task SeedTestDataAsync()
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderContext>();
            
            context.SubscriptionPlans.Add(new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Name = "Test Plan",
                Price = 9.99m,
                Description = "Test subscription plan"
            });
            
            await context.SaveChangesAsync();
        }
    }

    [Collection("Integration Tests")]
    public class StripeWebhookControllerTests : IAsyncLifetime
    {
        private readonly HttpClient _client;
        private readonly IntegrationTestFixture _fixture;
        private string _testSessionId = string.Empty;
        private readonly string _testUserId = "webhook_test_user";

        public StripeWebhookControllerTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.Factory.CreateClient();
        }

        public async Task InitializeAsync()
        {
            await _fixture.ResetDatabaseAsync();
            _testSessionId = await CreateTestCheckoutSession();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task HandleWebhook_ValidCheckoutSession_UpdatesSubscription()
        {
            // Arrange
            var jsonEvent = BuildCompletedSessionEvent();
            var signature = GenerateEventSignature(jsonEvent);

            // Act
            _client.DefaultRequestHeaders.Add("Stripe-Signature", signature);
            var response = await _client.PostAsync("/api/stripewebhook",
                new StringContent(jsonEvent, Encoding.UTF8, "application/json"));

            // Assert
            response.EnsureSuccessStatusCode();
            var subscription = await GetUserSubscription();
            Assert.Equal(SubscriptionStatus.Active, subscription?.Status);
        }

        [Fact]
        public async Task HandleWebhook_InvalidSignature_ReturnsBadRequest()
        {
            // Arrange
            var invalidSignature = "invalid_signature";
            var jsonEvent = BuildCompletedSessionEvent();

            // Act
            _client.DefaultRequestHeaders.Add("Stripe-Signature", invalidSignature);
            var response = await _client.PostAsync("/api/stripewebhook",
                new StringContent(jsonEvent, Encoding.UTF8, "application/json"));

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        private async Task<string> CreateTestCheckoutSession()
        {
            var client = _fixture.CreateAuthorizedClient(_testUserId);
            var request = new SubscriptionRequest { PriceId = "price_webhook_test" };
            
            var response = await client.PostAsJsonAsync("/api/subscription/create-checkout-session", request);
            var content = await response.Content.ReadFromJsonAsync<CreateCheckoutSessionResponseDto>();
            return content?.SessionId ?? string.Empty;
        }

        private string BuildCompletedSessionEvent() => $@"{{
            ""id"": ""evt_test_123"",
            ""object"": ""event"",
            ""type"": ""checkout.session.completed"",
            ""data"": {{
                ""object"": {{
                    ""id"": ""{_testSessionId}"",
                    ""subscription"": ""sub_test_123"",
                    ""payment_status"": ""paid"",
                    ""metadata"": {{
                        ""user_id"": ""{_testUserId}"",
                        ""signature"": ""{GenerateTestSignature()}""
                    }}
                }}
            }}
        }}";

        private string GenerateEventSignature(string jsonEvent)
        {
            var secret = _fixture.Configuration["Stripe:WebhookSecret"] ?? string.Empty;
            var signedPayload = $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.{jsonEvent}";
            return $"t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()},v1={CryptoHelper.ComputeHMACSHA256(secret, signedPayload)}";
        }

        private string GenerateTestSignature()
        {
            var secret = _fixture.Configuration["Stripe:MetadataSignatureSecret"] ?? string.Empty;
            return CryptoHelper.ComputeHMACSHA256(secret, _testUserId);
        }

        private async Task<haworks.Db.Subscription?> GetUserSubscription()
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IOrderContextRepository>();
            return await repo.GetSubscriptionByUserIdAsync(_testUserId);
        }
    }
}