using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace RitualWorks.Contracts
{
    public interface IImageService
    {
        Task<string> UploadImageAsync(Stream imageStream, string fileName);
    }
}

