// Services/FileValidator.cs
using System.Collections.Generic;
using System.IO;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using haworks.Contracts;
using haworks.Models;

namespace haworks.Services
{
    public class FileValidator : IFileValidator
    {
        private readonly IFileSignatureValidator _signatureValidator;
        private readonly IVirusScanner _virusScanner;
        private readonly ILogger<FileValidator> _logger;

        public FileValidator(
            IFileSignatureValidator signatureValidator,
            IVirusScanner virusScanner,
            ILogger<FileValidator> logger)
        {
            _signatureValidator = signatureValidator;
            _virusScanner = virusScanner;
            _logger = logger;
        }

        public async Task<FileValidationResult> ValidateAsync(IFormFile file)
        {
            var result = new FileValidationResult();
            
            try
            {
                if (file.Length == 0)
                {
                    result.AddError("File is empty");
                    return result;
                }

                await using var stream = file.OpenReadStream();
                var signatureResult = await _signatureValidator.ValidateAsync(stream);
                
                if (!signatureResult.IsValid)
                {
                    result.AddError("Invalid file signature");
                    return result;
                }

                stream.Position = 0;
                var scanResult = await _virusScanner.ScanAsync(stream);
                
                if (scanResult.IsMalicious)
                {
                    result.AddError("File contains malicious content");
                    return result;
                }

                return new FileValidationResult(
                    true,
                    signatureResult.FileType.ToString(),
                    Array.Empty<string>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File validation failed");
                result.AddError("Validation failed: " + ex.Message);
                return result;
            }
        }
    }
}