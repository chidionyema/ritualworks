using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using haworks.Contracts;
using haworks.Dto;
using haworks.Models;
using Xunit;
using Haworks.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using haworks.Db; 
using Haworks.Infrastructure.Data;
using haworks.Controllers;
using System.Text.Json;
using System.Net.Http.Json;

namespace Haworks.Tests.IntegrationTests.Controllers
{
    [Collection("Integration Tests")]
    public class ContentControllerTests : IAsyncLifetime
    {
        private readonly HttpClient _client;
        private readonly IntegrationTestFixture _fixture;

        public ContentControllerTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.Factory.WithTestAuth().CreateClient();
        }

        public async Task InitializeAsync()
        {
            // Reset DB first
            await _fixture.ResetDatabaseAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        // Helper: to create a multipart form request
        private MultipartFormDataContent CreateFileUploadContent(string fileName, string contentType, byte[] fileBytes)
        {
            var fileContent = new MultipartFormDataContent();
            var streamContent = new StreamContent(new MemoryStream(fileBytes));
            streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            fileContent.Add(streamContent, "file", fileName);
            return fileContent;
        }

        private async Task<string> DebugResponse(HttpResponseMessage response)
        {
            string content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Response: {response.StatusCode}");
            Console.WriteLine($"Content: {content}");
            return content;
        }

        [Fact]
        public async Task UploadFile_ValidFile_ReturnsCreatedContentAndStoresFile()
        {
            // Since FileSignatureValidator uses SixLabors.ImageSharp, use a JPEG file
            Guid entityId = Guid.NewGuid();
            string fileName = "test_integration.jpg";
            string contentType = "image/jpeg";
            
            // This is a minimal valid JPEG file header
            byte[] fileBytes = new byte[] {
                0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
                0x01, 0x01, 0x00, 0x48, 0x00, 0x48, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
                0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x01, 0x00, 0x01, 0x03, 0x01,
                0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01, 0xFF, 0xC4, 0x00, 0x14,
                0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x10, 0x01,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02,
                0x10, 0x03, 0x10, 0x00, 0x00, 0x01, 0x54, 0x00, 0xFF, 0xD9
            };
            
            var fileContent = CreateFileUploadContent(fileName, contentType, fileBytes);

            // Act
            var response = await _client.PostAsync($"/api/v1/content/upload?entityId={entityId}", fileContent);
            
            // Debug
            await DebugResponse(response);

            // Assert
            response.Should().HaveStatusCode(HttpStatusCode.Created);

            var contentDto = await response.Content.ReadFromJsonAsync<ContentDto>();
            contentDto.Should().NotBeNull();
            contentDto.EntityId.Should().Be(entityId);
            contentDto.EntityType.Should().Be("images"); // Changed to images since we're uploading a JPEG
            contentDto.ContentType.Should().Be("Image"); // Changed to Image
            contentDto.FileSize.Should().Be(fileBytes.Length);
            contentDto.Url.Should().NotBeNullOrEmpty();

            // Verify content is in DB
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ContentContext>();
                var content = await dbContext.Contents.FirstOrDefaultAsync(c => c.Id == contentDto.Id);
                content.Should().NotBeNull();
                content.ObjectName.Should().NotBeNullOrEmpty();
                content.BucketName.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public async Task UploadFile_InvalidFile_ReturnsBadRequest()
        {
            Guid entityId = Guid.NewGuid();
            string fileName = "test_integration.exe"; // Invalid file type
            string contentType = "application/x-executable";
            byte[] fileBytes = Encoding.UTF8.GetBytes("This is a fake executable.");
            var fileContent = CreateFileUploadContent(fileName, contentType, fileBytes);

            // Act
            var response = await _client.PostAsync($"/api/v1/content/upload?entityId={entityId}", fileContent);
            
            // Debug
            await DebugResponse(response);

            // Assert
            response.Should().HaveStatusCode(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task GetContent_ExistingContent_ReturnsContentDto()
        {
            Content contentToCreate;
            // Insert a content row manually
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ContentContext>();
                contentToCreate = new Content
                {
                    Id = Guid.NewGuid(),
                    EntityId = Guid.NewGuid(),
                    EntityType = "documents",
                    ContentType = ContentType.Document,
                    FileSize = 1024,
                    Path = "/test/path",
                    ObjectName = "test-object",
                    BucketName = "documents",
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.Contents.Add(contentToCreate);
                dbContext.SaveChanges();
            }

            // Act
            var response = await _client.GetAsync($"/api/v1/content/" + contentToCreate.Id);

            // Assert
            response.Should().HaveStatusCode(HttpStatusCode.OK);
            var contentDto = await response.Content.ReadFromJsonAsync<ContentDto>();
            contentDto.Should().NotBeNull();
            contentDto.Id.Should().Be(contentToCreate.Id);
        }

        [Fact]
        public async Task GetContent_NonExistingContent_ReturnsNotFound()
        {
            Guid contentId = Guid.NewGuid();

            var response = await _client.GetAsync($"/api/v1/content/" + contentId);

            response.Should().HaveStatusCode(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task DeleteContent_ExistingContent_ReturnsNoContentAndDeletesFromDb()
        {
            Content contentToDelete;
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ContentContext>();
                contentToDelete = new Content
                {
                    Id = Guid.NewGuid(),
                    EntityId = Guid.NewGuid(),
                    EntityType = "documents",
                    ContentType = ContentType.Document,
                    FileSize = 1024,
                    Path = "/test/path",
                    ObjectName = "test-object-delete",
                    BucketName = "documents",
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.Contents.Add(contentToDelete);
                dbContext.SaveChanges();
            }

            // Act
            var response = await _client.DeleteAsync($"/api/v1/content/" + contentToDelete.Id);

            // Assert
            response.Should().HaveStatusCode(HttpStatusCode.NoContent);
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ContentContext>();
                var content = await dbContext.Contents.FirstOrDefaultAsync(c => c.Id == contentToDelete.Id);
                content.Should().BeNull();
            }
        }

        [Fact]
        public async Task DeleteContent_NonExistingContent_ReturnsNotFound()
        {
            Guid contentId = Guid.NewGuid();
            var response = await _client.DeleteAsync($"/api/v1/content/" + contentId);
            response.Should().HaveStatusCode(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task InitChunkSession_ValidRequest_ReturnsCreatedSessionAndStoresInRedis()
        {
            Guid entityId = Guid.NewGuid();
            var request = new ChunkSessionRequest(
                entityId,
                "largefile_integration.mp4",
                10,
                1024 * 1024 * 100);

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await _client.PostAsync("/api/v1/content/chunked/init", content);
            
            // Debug
            await DebugResponse(response);

            // Assert
            response.Should().HaveStatusCode(HttpStatusCode.Created);
            var sessionResponse = await response.Content.ReadFromJsonAsync<ChunkSession>();
            sessionResponse.Should().NotBeNull();
            sessionResponse.Id.Should().NotBeEmpty();
            sessionResponse.EntityId.Should().Be(request.EntityId);

            // Verify session in Redis
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var redis = scope.ServiceProvider
                    .GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>()
                    .GetDatabase();
                var sessionJson = await redis.StringGetAsync($"chunkSession:{sessionResponse.Id}");
                sessionJson.HasValue.Should().BeTrue();

                var sessionRedis = JsonSerializer.Deserialize<ChunkSession>(sessionJson);
                sessionRedis.Should().BeEquivalentTo(
                    sessionResponse,
                    options => options.Excluding(o => o.ExpiresAt));
            }
        }

        [Fact]
        public async Task InitChunkSession_InvalidRequest_ReturnsBadRequest()
        {
            Guid entityId = Guid.NewGuid();
            
            // Use negative totalChunks to force a validation error
            var request = new ChunkSessionRequest(
                entityId,
                "test.mp4",
                TotalChunks: -1, // Negative, should be invalid
                TotalSize: 1024 * 1024);

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            var response = await _client.PostAsync("/api/v1/content/chunked/init", content);
            
            // Debug
            await DebugResponse(response);
            
            response.Should().HaveStatusCode(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task UploadChunk_ValidChunk_ReturnsOkAndStoresChunk()
        {
            // Use a simpler approach without relying on file signature validation
            Guid entityId = Guid.NewGuid();
            int totalChunks = 3;
            int chunkSize = 1024; // 1KB
            int totalSize = totalChunks * chunkSize; // 3KB
            
            var initReq = new ChunkSessionRequest(
                entityId,
                "test_video.mp4",
                TotalChunks: totalChunks,
                TotalSize: totalSize);

            var initContent = new StringContent(JsonSerializer.Serialize(initReq), Encoding.UTF8, "application/json");
            var initResp = await _client.PostAsync("/api/v1/content/chunked/init", initContent);
            
            Console.WriteLine($"Init response: {initResp.StatusCode}");
            await DebugResponse(initResp);
            
            var sessionResponse = await initResp.Content.ReadFromJsonAsync<ChunkSession>();
            var sessionId = sessionResponse.Id;

            int chunkIndex = 0;
            
            // Create a chunk with exactly the right size
            byte[] chunkBytes = new byte[chunkSize];
            new Random().NextBytes(chunkBytes); // Fill with random data
            
            var chunkContent = CreateFileUploadContent("chunkFile", "application/octet-stream", chunkBytes);

            // Act
            var response = await _client.PostAsync($"/api/v1/content/chunked/{sessionId}/{chunkIndex}", chunkContent);
            
            // Debug
            await DebugResponse(response);

            // Assert
            response.Should().HaveStatusCode(HttpStatusCode.OK);

            // Verify chunk is in MinIO
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var storageService = scope.ServiceProvider.GetRequiredService<IContentStorageService>();
                var downloadedStream = await storageService.DownloadAsync("temp-chunks", $"chunks/{sessionId}/{chunkIndex}");
                downloadedStream.Should().NotBeNull();
                downloadedStream.Dispose();
            }
        }

        [Fact]
        public async Task UploadChunk_InvalidSession_ReturnsNotFound()
        {
            // Create a minimal but valid chunk to test just the session not found condition
            Guid sessionId = Guid.NewGuid(); // Non-existent session
            int chunkIndex = 0;
            
            byte[] chunkBytes = new byte[1024]; // 1KB
            new Random().NextBytes(chunkBytes); // Fill with random data
            
            var chunkContent = CreateFileUploadContent("chunkFile", "application/octet-stream", chunkBytes);

            var response = await _client.PostAsync($"/api/v1/content/chunked/{sessionId}/{chunkIndex}", chunkContent);
            
            // Debug
            await DebugResponse(response);
            
            response.Should().HaveStatusCode(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task CompleteChunkSession_ValidSession_ReturnsCreatedContentAndAssemblesFile()
        {
            // Use a simplified approach with small chunks
            Guid entityId = Guid.NewGuid();
            string fileName = "test_small_video.mp4";
            int totalChunks = 3;
            int chunkSize = 1024; // 1KB
            int totalSize = totalChunks * chunkSize; // 3KB total

            var initReq = new ChunkSessionRequest(entityId, fileName, totalChunks, totalSize);
            var initResp = await _client.PostAsJsonAsync("/api/v1/content/chunked/init", initReq);
            
            Console.WriteLine($"Init response: {initResp.StatusCode}");
            await DebugResponse(initResp);
            
            var sessionResponse = await initResp.Content.ReadFromJsonAsync<ChunkSession>();
            Guid sessionId = sessionResponse.Id;

            // Upload all chunks with identical size
            for (int i = 0; i < totalChunks; i++)
            {
                byte[] chunkBytes = new byte[chunkSize];
                new Random().NextBytes(chunkBytes); // Fill with random data
                
                var chunkContent = CreateFileUploadContent("chunkFile", "application/octet-stream", chunkBytes);
                var chunkResp = await _client.PostAsync($"/api/v1/content/chunked/{sessionId}/{i}", chunkContent);
                
                Console.WriteLine($"Chunk {i} response: {chunkResp.StatusCode}");
                await DebugResponse(chunkResp);
                
                chunkResp.EnsureSuccessStatusCode();
            }

            // Complete the session
            var response = await _client.PostAsync($"/api/v1/content/chunked/complete/{sessionId}", null);
            
            // Debug
            await DebugResponse(response);

            // Assert
            response.Should().HaveStatusCode(HttpStatusCode.Created);
            var contentDto = await response.Content.ReadFromJsonAsync<ContentDto>();
            contentDto.Should().NotBeNull();
            contentDto.Id.Should().NotBeEmpty();
            contentDto.EntityType.Should().Be("Content");
            contentDto.ContentType.Should().Be("Video");
            contentDto.FileSize.Should().Be(totalSize);
            contentDto.Url.Should().NotBeNullOrEmpty();

            // verify DB
            using (var scope = _fixture.Factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ContentContext>();
                var contentRow = await dbContext.Contents.FirstOrDefaultAsync(c => c.Id == contentDto.Id);
                contentRow.Should().NotBeNull();
                contentRow.ObjectName.Should().NotBeNullOrEmpty();
                contentRow.BucketName.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public async Task CompleteChunkSession_IncompleteSession_ReturnsBadRequest()
        {
            Guid entityId = Guid.NewGuid();
            var initReq = new ChunkSessionRequest(entityId, "largefile_incomplete_integration.mp4", TotalChunks: 3, TotalSize: 3 * 1024 * 1024);
            var initResp = await _client.PostAsJsonAsync("/api/v1/content/chunked/init", initReq);
            var sessionResponse = await initResp.Content.ReadFromJsonAsync<ChunkSession>();
            var sessionId = sessionResponse.Id;

            // We did NOT upload the required 3 chunks

            var response = await _client.PostAsync($"/api/v1/content/chunked/complete/{sessionId}", null);
            
            // Debug
            await DebugResponse(response);
            
            response.Should().HaveStatusCode(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task GetChunkSessionStatus_ExistingSession_ReturnsSessionStatus()
        {
            Guid entityId = Guid.NewGuid();
            var initReq = new ChunkSessionRequest(entityId, "largefile_status_integration.mp4", 3, 3 * 1024 * 1024);
            var initResp = await _client.PostAsJsonAsync("/api/v1/content/chunked/init", initReq);
            var sessionResponse = await initResp.Content.ReadFromJsonAsync<ChunkSession>();
            var sessionId = sessionResponse.Id;

            var response = await _client.GetAsync($"/api/v1/content/chunked/session/{sessionId}");
            response.Should().HaveStatusCode(HttpStatusCode.OK);
            var sessionStatus = await response.Content.ReadFromJsonAsync<ChunkSession>();
            sessionStatus.Should().NotBeNull();
            sessionStatus.Id.Should().Be(sessionId);
        }

        [Fact]
        public async Task GetChunkSessionStatus_NonExistingSession_ReturnsNotFound()
        {
            Guid sessionId = Guid.NewGuid(); // never in Redis
            var response = await _client.GetAsync($"/api/v1/content/chunked/session/{sessionId}");
            response.Should().HaveStatusCode(HttpStatusCode.NotFound);
        }
    }
}