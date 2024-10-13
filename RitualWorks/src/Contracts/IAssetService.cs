using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace RitualWorks.Services
{
    public interface IAssetService
    {
        Task<string> UploadFileAsync(IFormFile? file, Guid productId , string username);
        bool ValidateFile(IFormFile file, out string validationError, out string fileType);
    }
}
