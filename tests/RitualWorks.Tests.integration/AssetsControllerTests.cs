using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using haworks.Controllers;

namespace haworks.Tests
{
    [Collection("Integration Tests")]
    public class AssetsControllerTests : IDisposable
    {
        private readonly HttpClient _client;
        private readonly List<string> _tempDirectories = new List<string>();

        public AssetsControllerTests(IntegrationTestFixture fixture)
        {
            var factory = fixture.CreateFactory();
            _client = factory.CreateClient();

             _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        }

        [Fact]
        public async Task UploadFile_ShouldReturnOk_WhenFileIsValid()
        {
            // Arrange
            var uniqueDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(uniqueDirectory);
            _tempDirectories.Add(uniqueDirectory);

            // Use an allowed file extension, e.g., .pdf
            var fileExtension = ".pdf";
            var fileName = $"testfile_{Guid.NewGuid()}{fileExtension}";
            var filePath = Path.Combine(uniqueDirectory, fileName);
            await File.WriteAllTextAsync(filePath, "Test content");

            var productId = Guid.NewGuid();
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(productId.ToString()), nameof(FileUploadDto.ProductId));
            form.Add(new StringContent("testuser"), nameof(FileUploadDto.Username));
            form.Add(new StreamContent(File.OpenRead(filePath))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
            }, nameof(FileUploadDto.File), fileName); // Corrected field name to match DTO properties

            // Act
            var response = await _client.PostAsync("/api/assets/upload", form);

            // Log error content if the response is not OK
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error Content: {errorContent}");
            }

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<dynamic>();
            result.Should().NotBeNull();
            string returnedFileName = result.fileName ?? result.FileName;
            returnedFileName.Should().Be($"testuser/products/assets/{fileName}");
        }

        [Fact]
        public async Task UploadFileChunk_ShouldReturnOk_WhenFileChunkIsUploaded()
        {
            // Arrange
            var uniqueDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(uniqueDirectory);
            _tempDirectories.Add(uniqueDirectory);

            // Use an allowed file extension, e.g., .pdf
            var fileExtension = ".pdf";
            var fileName = $"chunkfile_{Guid.NewGuid()}{fileExtension}";
            var filePath = Path.Combine(uniqueDirectory, fileName);
            await File.WriteAllTextAsync(filePath, "Chunk content");

            var productId = Guid.NewGuid();
            var form = new MultipartFormDataContent();
            form.Add(new StringContent("0"), nameof(FileChunkUploadDto.ChunkIndex));
            form.Add(new StringContent("1"), nameof(FileChunkUploadDto.TotalChunks));
            form.Add(new StringContent(fileName), nameof(FileChunkUploadDto.FileName));
            form.Add(new StringContent(productId.ToString()), nameof(FileChunkUploadDto.ProductId));
            form.Add(new StringContent("testuser"), nameof(FileChunkUploadDto.Username));
            form.Add(new StreamContent(File.OpenRead(filePath))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
            }, nameof(FileChunkUploadDto.File), fileName); // Corrected field name to match DTO properties

            // Act
            var response = await _client.PostAsync("/api/assets/upload-chunk", form);

            // Log error content if the response is not OK
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Error Content: {errorContent}");
            }

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task UploadFile_ShouldReturnBadRequest_WhenFileIsInvalid()
        {
            // Arrange
            var productId = Guid.NewGuid();
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(productId.ToString()), nameof(FileUploadDto.ProductId));
            form.Add(new StringContent("testuser"), nameof(FileUploadDto.Username));
            form.Add(new StreamContent(Stream.Null)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
            }, nameof(FileUploadDto.File), "invalidfile.pdf"); // Corrected field name

            // Act
            var response = await _client.PostAsync("/api/assets/upload", form);

            // Log error content if the response is not BadRequest
            if (response.StatusCode != HttpStatusCode.BadRequest)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Unexpected Response Content: {errorContent}");
            }

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        public void Dispose()
        {
            // Clean up temporary directories
            foreach (var dir in _tempDirectories)
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
    }
}
