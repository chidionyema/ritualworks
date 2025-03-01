using SixLabors.ImageSharp;
using System.IO;
using System.Threading.Tasks;
using haworks.Contracts;
using haworks.Models;

namespace haworks.Services
{
    public class FileSignatureValidator : IFileSignatureValidator
    {
        public async Task<FileSignatureValidationResult> ValidateAsync(Stream fileStream)
        {
            try
            {
                // Reset stream position for validation
                fileStream.Position = 0;
                
                // Try image validation
                var imageInfo = await Image.IdentifyAsync(fileStream);
                if (imageInfo != null)
                {
                    return new FileSignatureValidationResult(true, "image");
                }

                // Add other file type validations here
                
                return new FileSignatureValidationResult(false, "unknown");
            }
            catch
            {
                return new FileSignatureValidationResult(false, "invalid");
            }
        }
    }
}