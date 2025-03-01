using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace haworks.Tests.Common
{
    public static class CertificateHelper
    {
        public static X509Certificate2 CreateValidTestCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "cn=valid-test-cert",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            
            var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddYears(1));
            
            return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
        }

        public static X509Certificate2 CreateExpiredTestCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "cn=expired-test-cert",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            
            var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddYears(-2),
                DateTimeOffset.UtcNow.AddYears(-1));
            
            return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
        }
    }

    public static class SystemTime
    {
        public static Func<DateTime> Now = () => DateTime.UtcNow;

        public static void Set(DateTime time) => Now = () => time;
        public static void Reset() => Now = () => DateTime.UtcNow;
    }
}