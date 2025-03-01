using System.Security.Cryptography.X509Certificates;
using haworks.Services;
using haworks.Tests.Common;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace haworks.Tests.Security
{
    public class CertificateValidatorTests : TestBase
    {
        public CertificateValidatorTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void ValidateCertificate_ValidCert_ReturnsTrue()
        {
            // Arrange
            using var cert = CertificateHelper.CreateValidTestCertificate();
            var validator = new CertificateValidator();

            // Act
            var result = validator.ValidateCertificate(
                cert, 
                cert.Thumbprint, 
                LoggerFactory.CreateLogger<CertificateValidator>());

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ValidateCertificate_ExpiredCert_ReturnsFalse()
        {
            // Arrange
            using var cert = CertificateHelper.CreateExpiredTestCertificate();
            var validator = new CertificateValidator();

            // Act
            var result = validator.ValidateCertificate(
                cert, 
                cert.Thumbprint, 
                LoggerFactory.CreateLogger<CertificateValidator>());

            // Assert
            Assert.False(result);
        }
    }
}