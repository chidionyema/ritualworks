using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
namespace RitualWorks.Services
{
    public interface ISignedUrlService
    {
        string GenerateSignedUrl(string filePath, TimeSpan validFor);
        bool TryValidateSignedUrl(string url, out string filePath);
    }

    public class SignedUrlService : ISignedUrlService
    {
        private readonly string _secretKey;

        public SignedUrlService(IConfiguration configuration)
        {
            _secretKey = configuration["SignedUrl:SecretKey"];
        }

        public string GenerateSignedUrl(string filePath, TimeSpan validFor)
        {
            var expiry = DateTimeOffset.UtcNow.Add(validFor).ToUnixTimeSeconds();
            var url = $"{filePath}?expiry={expiry}";
            var signature = Sign(url, _secretKey);

            return $"{url}&signature={signature}";
        }

        public bool TryValidateSignedUrl(string url, out string filePath)
        {
            filePath = null;
            var uri = new Uri(url);
            var queryParams = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);

            if (!queryParams.TryGetValue("expiry", out var expiryValue) ||
                !queryParams.TryGetValue("signature", out var signatureValue))
            {
                return false;
            }

            var expiry = long.Parse(expiryValue);
            var signature = signatureValue;
            var currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (currentUnixTime > expiry)
            {
                return false;
            }

            var urlWithoutSignature = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}?expiry={expiry}";
            var expectedSignature = Sign(urlWithoutSignature, _secretKey);

            if (signature != expectedSignature)
            {
                return false;
            }

            filePath = uri.AbsolutePath;
            return true;
        }

        private string Sign(string data, string key)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash);
        }
    }

}

