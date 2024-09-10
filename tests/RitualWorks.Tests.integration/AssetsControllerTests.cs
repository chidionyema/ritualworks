using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using FluentAssertions;

namespace RitualWorks.Tests
{
    [Collection("Integration Tests")]
    public class AssetsControllerTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public AssetsControllerTests(WebApplicationFactory<Program> factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task UploadFile_ShouldReturnOk_WhenFileIsValid()
        {
            // Arrange
            var filePath = Path.GetTempFileName();
            await File.WriteAllTextAsync(filePath, "Test content");

            var productId = Guid.NewGuid();
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(productId.ToString()), "productId");
            form.Add(new StringContent("testuser"), "username");
            form.Add(new StreamContent(File.OpenRead(filePath))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("text/plain") }
            }, "file", "testfile.txt");

            // Act
            var response = await _client.PostAsync("/api/assets/upload", form);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<dynamic>();
            result.Should().NotBeNull();
            result.FileName.Should().Be("testuser/products/assets/testfile.txt");
        }

        [Fact]
        public async Task UploadFileChunk_ShouldReturnOk_WhenFileChunkIsUploaded()
        {
            // Arrange
            var filePath = Path.GetTempFileName();
            await File.WriteAllTextAsync(filePath, "Chunk content");

            var productId = Guid.NewGuid();
            var form = new MultipartFormDataContent();
            form.Add(new StringContent("0"), "chunkIndex");
            form.Add(new StringContent("1"), "totalChunks");
            form.Add(new StringContent("chunkfile.txt"), "fileName");
            form.Add(new StringContent(productId.ToString()), "productId");
            form.Add(new StringContent("testuser"), "username");
            form.Add(new StreamContent(File.OpenRead(filePath))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("text/plain") }
            }, "file", "chunkfile.txt");

            // Act
            var response = await _client.PostAsync("/api/assets/upload-chunk", form);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task UploadFile_ShouldReturnBadRequest_WhenFileIsInvalid()
        {
            // Arrange
            var productId = Guid.NewGuid();
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(productId.ToString()), "productId");
            form.Add(new StringContent("testuser"), "username");
            form.Add(new StreamContent(Stream.Null)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("text/plain") }
            }, "file", "invalidfile.txt");

            // Act
            var response = await _client.PostAsync("/api/assets/upload", form);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
