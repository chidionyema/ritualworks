using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace haworks.Services
{
    public interface IContentService
    {
        Task<string> UploadFileAsync(IFormFile? file, Guid productId , string username);
        bool ValidateFile(IFormFile file, out string error, out bool isImage);
        Task<string> GetPreSignedUrlAsync(string filePath, int expiryInSeconds = 3600);
    }
}
