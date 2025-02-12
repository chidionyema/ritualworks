using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using haworks.Controllers; // Ensure this namespace contains ValidateStripeWebhookAttribute.
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Xunit;
using Stripe;

namespace haworks.Tests
{
    public class ValidateStripeWebhookAttributeTests
    {
        [Fact]
        public async Task OnActionExecutionAsync_ReturnsBadRequest_WhenSignatureInvalid()
        {
            // Arrange: Create an instance of our derived attribute with a known webhook secret.
            var attribute = new TestValidateStripeWebhookAttribute("whsec_test");

            // Create a fake HTTP request with an invalid signature.
            var httpContext = new DefaultHttpContext();
            string bodyContent = "{}";
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(bodyContent));
            httpContext.Request.Headers["Stripe-Signature"] = "invalid_signature";

            var actionContext = new ActionContext { HttpContext = httpContext };
            var actionExecutingContext = new ActionExecutingContext(
                actionContext, 
                new List<IFilterMetadata>(), 
                new Dictionary<string, object?>(), 
                new object());
            bool nextCalled = false;
            ActionExecutionDelegate next = () =>
            {
                nextCalled = true;
                var executedContext = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object());
                return Task.FromResult(executedContext);
            };

            // Act
            await attribute.OnActionExecutionAsync(actionExecutingContext, next);

            // Assert: Expect a BadRequestObjectResult since the signature is invalid.
            Assert.IsType<BadRequestObjectResult>(actionExecutingContext.Result);
            Assert.False(nextCalled);
        }

        [Fact]
        public async Task OnActionExecutionAsync_CallsNext_WhenSignatureValid()
        {
            // Arrange: Create an instance of our derived attribute with a known webhook secret.
            var webhookSecret = "whsec_test";
            var attribute = new TestValidateStripeWebhookAttribute(webhookSecret);

            // Construct a minimal valid Stripe event payload.
            var stripeEvent = new Event
            {
                Id = "evt_test",
                Type = "checkout.session.completed",
                // Use current timestamp for the created field.
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Data = new EventData
                {
                    // For testing, a minimal Session object.
                    Object = new Session
                    {
                        Id = "cs_test"
                        // Populate additional required properties if needed.
                    }
                }
            };

            // Serialize the event to JSON.
            var jsonPayload = JsonSerializer.Serialize(stripeEvent);

            // Compute a valid signature using our test helper.
            var validSignature = ComputeTestSignature(webhookSecret, jsonPayload);

            // Create a fake HTTP request with the valid payload and signature.
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonPayload));
            httpContext.Request.Headers["Stripe-Signature"] = validSignature;

            var actionContext = new ActionContext { HttpContext = httpContext };
            var actionExecutingContext = new ActionExecutingContext(
                actionContext, 
                new List<IFilterMetadata>(), 
                new Dictionary<string, object?>(), 
                new object());
            bool nextCalled = false;
            ActionExecutionDelegate next = () =>
            {
                nextCalled = true;
                var executedContext = new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object());
                return Task.FromResult(executedContext);
            };

            // Act
            await attribute.OnActionExecutionAsync(actionExecutingContext, next);

            // Assert: With a valid signature the attribute should call next() and not set a Result.
            Assert.Null(actionExecutingContext.Result);
            Assert.True(nextCalled);
        }

        /// <summary>
        /// Computes a test signature that mimics Stripe’s expected header format.
        /// For Stripe, the header is typically in the format: "t=timestamp,v1=signature".
        /// The signature is computed as an HMAC-SHA256 of "timestamp.payload" using the webhook secret.
        /// </summary>
        private string ComputeTestSignature(string secret, string payload)
        {
            // Use current timestamp.
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var payloadToSign = $"{timestamp}.{payload}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadToSign));
            var computedSignature = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            return $"t={timestamp},v1={computedSignature}";
        }

        // A derived attribute class to inject IConfiguration into the base attribute.
        private class TestValidateStripeWebhookAttribute : ValidateStripeWebhookAttribute
        {
            public TestValidateStripeWebhookAttribute(string webhookSecret)
            {
                // Build an in-memory configuration using a Dictionary<string, string>.
                var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "Stripe:WebhookSecret", webhookSecret }
                }).Build();

                // Use reflection to set the private _configuration field in the base attribute.
                var field = typeof(ValidateStripeWebhookAttribute)
                    .GetField("_configuration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                field?.SetValue(this, config);
            }
        }
    }
}
