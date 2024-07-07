using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.Controllers;
using RitualWorks.Db;

namespace RitualWorks.Services
{

    public interface IBlobStorageService
    {
        Task CombineChunks(string uploadPath, string fileName, int totalChunks);
        Task UploadToBlobStorage(string filePath, string blobName);
    }
}
