using Microsoft.AspNetCore.Http;
using System;
using System.IO;           
using System.Threading;
using System.Threading.Tasks;

namespace haworks.Models
{
   public class PhysicalFileFormFile : IFormFile
   {
    private readonly string _filePath;
    private readonly string _fileName;

    public PhysicalFileFormFile(string filePath, string fileName, long length)
    {
        _filePath = filePath;
        _fileName = fileName;
        Length = length;
    }

    public string ContentType => "application/octet-stream";
    public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{_fileName}\"";
    public IHeaderDictionary Headers => null;
    public long Length { get; }
    public string Name => "file";
    public string FileName => _fileName;

    public Stream OpenReadStream() => new FileStream(_filePath, FileMode.Open, FileAccess.Read);
    public void CopyTo(Stream target) => throw new NotImplementedException();
    public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => throw new NotImplementedException();
  }

}
