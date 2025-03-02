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
                // Log the file name at the start of the validation
                _logger.LogInformation("Starting file validation for file: {FileName}", file.FileName);

                if (file.Length == 0)
                {
                    string errorMessage = "File is empty";
                    _logger.LogWarning("Validation failed for file {FileName}: {ErrorMessage}", file.FileName, errorMessage);
                    result.AddError(errorMessage);
                    return result;
                }

                await using var stream = file.OpenReadStream();
                var signatureResult = await _signatureValidator.ValidateAsync(stream);
                
                if (!signatureResult.IsValid)
                {
                    string errorMessage = "Invalid file signature";
                    _logger.LogWarning("Validation failed for file {FileName}: {ErrorMessage}", file.FileName, errorMessage);
                    result.AddError(errorMessage);
                    return result;
                }

                stream.Position = 0;
                var scanResult = await _virusScanner.ScanAsync(stream);
                
                if (scanResult.IsMalicious)
                {
                    string errorMessage = "File contains malicious content";
                    _logger.LogWarning("Validation failed for file {FileName}: {ErrorMessage}", file.FileName, errorMessage);
                    result.AddError(errorMessage);
                    return result;
                }

                _logger.LogInformation("File validation succeeded for file: {FileName}", file.FileName);
                return new FileValidationResult(
                    true,
                    signatureResult.FileType.ToString(),
                    Array.Empty<string>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File validation failed for file {FileName}", file.FileName);
                result.AddError("Validation failed: " + ex.Message);
                return result;
            }
        }
    }
}
