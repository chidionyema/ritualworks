using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;

namespace haworks.Tests.Common
{
    public abstract class TestBase : IDisposable
    {
        protected readonly ITestOutputHelper Output;
        protected readonly MockRepository MockRepository;
        protected readonly IConfiguration TestConfig;
        protected readonly ILoggerFactory LoggerFactory;

        protected TestBase(ITestOutputHelper output)
        {
            Output = output;
            MockRepository = new MockRepository(MockBehavior.Strict);
            
            TestConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Vault:Address"] = "https://vault-test:8200",
                    ["Vault:RoleIdPath"] = "test-data/role.id",
                    ["Vault:SecretIdPath"] = "test-data/secret.id",
                    ["Vault:ServerCertThumbprint"] = "TEST_THUMBPRINT",
                    ["Resilience:MaxRetries"] = "3",
                    ["Resilience:FailureThreshold"] = "0.6"
                })
                .Build();

            LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                builder.AddXUnit(Output);
                builder.SetMinimumLevel(LogLevel.Debug);
            });
        }

        protected Mock<IVaultClient> CreateVaultClientMock()
        {
            var mock = MockRepository.Create<IVaultClient>();
            // Default setup for successful response
            mock.Setup(v => v.V1.Secrets.Database.GetCredentialsAsync(
                    It.IsAny<string>(), 
                    It.IsAny<string>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Secret<UsernamePasswordCredentials> 
                { 
                    Data = new UsernamePasswordCredentials 
                    { 
                        Username = "test_user", 
                        Password = "test_password" 
                    },
                    LeaseDurationSeconds = 3600
                });
            return mock;
        }

        public virtual void Dispose()
        {
            MockRepository.VerifyAll();
        }
    }
}