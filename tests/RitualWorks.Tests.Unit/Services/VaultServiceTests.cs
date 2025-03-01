using System.Security;
using System.Threading.Tasks;
using haworks.Services;
using haworks.Tests.Common;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace haworks.Tests.Services
{
    public class VaultServiceTests : TestBase
    {
        public VaultServiceTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task GetDatabaseCredentialsAsync_ReturnsValidCredentials()
        {
            // Arrange
            var mockVault = CreateVaultClientMock();
            var service = new VaultService(TestConfig, LoggerFactory.CreateLogger<VaultService>(), mockVault.Object);

            // Act
            var (username, password) = await service.GetDatabaseCredentialsAsync();

            // Assert
            Assert.Equal("test_user", username);
            Assert.True(password.Length > 0);
        }

        [Fact]
        public async Task GetDatabaseCredentialsAsync_WhenExpired_RefreshesCredentials()
        {
            // Arrange
            var mockVault = CreateVaultClientMock();
            var service = new VaultService(TestConfig, LoggerFactory.CreateLogger<VaultService>(), mockVault.Object);
            
            // Initial credentials
            await service.GetDatabaseCredentialsAsync();

            // Force expiration
            var expiredTime = DateTime.UtcNow.AddHours(2);
            SystemTime.Set(expiredTime);

            // Act
            var (username, _) = await service.GetDatabaseCredentialsAsync();

            // Assert
            mockVault.Verify(v => v.V1.Secrets.Database.GetCredentialsAsync(
                It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ConcurrentAccess_SerializesCredentialRefresh()
        {
            // Arrange
            var mockVault = CreateVaultClientMock();
            var service = new VaultService(TestConfig, LoggerFactory.CreateLogger<VaultService>(), mockVault.Object);
            var parallelTasks = 10;

            // Act
            var tasks = Enumerable.Range(0, parallelTasks)
                .Select(_ => service.GetDatabaseCredentialsAsync())
                .ToList();

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, r => Assert.Equal("test_user", r.Username));
            mockVault.Verify(v => v.V1.Secrets.Database.GetCredentialsAsync(
                It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}