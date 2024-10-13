using Microsoft.Extensions.Configuration;
using RitualWorks.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace RitualWorks.Services
{
    public class LocalFileSystemService : IFileStorageService
    {
        private readonly string _fileDirectory;
        private readonly ISignedUrlService _signedUrlService;

        public LocalFileSystemService(IConfiguration configuration, ISignedUrlService signedUrlService)
        {
            _fileDirectory = configuration["LocalFileStorage:Directory"];
            _signedUrlService = signedUrlService;
        }

        public async Task<string> UploadFileAsync(Stream fileStream, string filePath, bool append = false)
        {
            var fullPath = Path.Combine(_fileDirectory, filePath.Replace("\\", "/"));
            var directoryPath = Path.GetDirectoryName(fullPath);

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            using (var fileStreamLocal = new FileStream(fullPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write))
            {
                await fileStream.CopyToAsync(fileStreamLocal);
            }
            return filePath;
        }

        public async Task<Stream> DownloadFile(string filePath)
        {
            var fullPath = Path.Combine(_fileDirectory, filePath.Replace("\\", "/"));
            return new FileStream(fullPath, FileMode.Open, FileAccess.Read);
        }

        public async Task<bool> DeleteFileAsync(string filePath)
        {
            var fullPath = Path.Combine(_fileDirectory, filePath.Replace("\\", "/"));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return true;
            }
            return false;
        }

        public string GenerateSignedUrl(string filePath, TimeSpan validFor)
        {
            var fullPath = Path.Combine(_fileDirectory, filePath.Replace("\\", "/"));
            return _signedUrlService.GenerateSignedUrl(fullPath, validFor);
        }

        public bool TryValidateSignedUrl(string url, out string filePath)
        {
            return _signedUrlService.TryValidateSignedUrl(url, out filePath);
        }

        public Task CommitAsync(string filePath, List<string> blockIds)
        {
            throw new NotImplementedException();
        }
    }
}
