using System;
using System.Net.Http;
using System.Threading.Tasks;
using haworks.Services;
using haworks.Tests.Common;
using Microsoft.Extensions.Logging;
using Moq;
using Polly;
using Polly.CircuitBreaker;
using Xunit;
using Xunit.Abstractions;

namespace haworks.Tests.Resilience
{
    public class ResiliencePolicyTests : TestBase
    {
        public ResiliencePolicyTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task Policy_RetriesOnTransientErrors()
        {
            // Arrange
            var mockVault = CreateVaultClientMock();
            mockVault.SetupSequence(v => v.V1.Secrets.Database.GetCredentialsAsync(
                    It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException())
                .ThrowsAsync(new HttpRequestException())
                .ReturnsAsync(new Secret<UsernamePasswordCredentials> 
                { 
                    Data = new UsernamePasswordCredentials 
                    { 
                        Username = "retry_user", 
                        Password = "retry_pass" 
                    } 
                });

            var service = new VaultService(TestConfig, LoggerFactory.CreateLogger<VaultService>(), mockVault.Object);

            // Act
            var result = await service.GetDatabaseCredentialsAsync();

            // Assert
            Assert.Equal("retry_user", result.Username);
        }

        [Fact]
        public async Task CircuitBreaker_OpensAfterThreshold()
        {
            // Arrange
            var mockVault = CreateVaultClientMock();
            mockVault.Setup(v => v.V1.Secrets.Database.GetCredentialsAsync(
                    It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException());

            var service = new VaultService(TestConfig, LoggerFactory.CreateLogger<VaultService>(), mockVault.Object);

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(() => service.GetDatabaseCredentialsAsync());
            await Assert.ThrowsAsync<HttpRequestException>(() => service.GetDatabaseCredentialsAsync());
            await Assert.ThrowsAsync<BrokenCircuitException>(() => service.GetDatabaseCredentialsAsync());
        }
    }
}