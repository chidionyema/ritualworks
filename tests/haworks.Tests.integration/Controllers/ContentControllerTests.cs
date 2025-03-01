using System;
using System.Collections.Generic;
using System.IO;
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

            // Create a client that shares the CookieContainer:
            _client = fixture.CreateClientWithCookies();

            // If you have a custom test auth scheme called "Test", you can set that here,
            // though it may not be strictly necessary if your real cookie is used:
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Test");
        }

        public async Task InitializeAsync()
        {
            // 1) Reset DB first
            await _fixture.ResetDatabaseAsync();

            // 2) Now register a user with "ContentUploader" role (your register endpoint does that).
            var username = "content_tester_" + Guid.NewGuid();
            var password = "Password123!";
            var regDto = new UserRegistrationDto
            {
                Username = username,
                Email = $"{username}@test.com",
                Password = password
            };
            // /register => sets the user in DB with "ContentUploader" + claim + sets the JWT cookie
            var regResp = await _client.PostAsJsonAsync("/api/authentication/register", regDto);
            regResp.EnsureSuccessStatusCode();

            // If your /register does NOT auto-log the user in, then do /login here:
            // var loginDto = new UserLoginDto { Username = username, Password = password };
            // var loginResp = await _client.PostAsJsonAsync("/api/authentication/login", loginDto);
            // loginResp.EnsureSuccessStatusCode();
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

        [Fact]
        public async Task UploadFile_ValidFile_ReturnsCreatedContentAndStoresFile()
        {
            // Because the user was registered in InitializeAsync(),
            // the cookie is set => user is [Authorize(Policy="ContentUploader")].
            Guid entityId = Guid.NewGuid();
            string fileName = "test_integration.pdf";
            string contentType = "application/pdf";
            byte[] fileBytes = Encoding.UTF8.GetBytes("This is a test pdf file for integration test.");
            var fileContent = CreateFileUploadContent(fileName, contentType, fileBytes);

            // Act
            var response = await _client.PostAsync($"/api/v1/content/upload?entityId={entityId}", fileContent);

            // Assert
            response.Should().HaveStatusCode(HttpStatusCode.Created);

            var contentDto = await response.Content.ReadFromJsonAsync<ContentDto>();
            contentDto.Should().NotBeNull();
            contentDto.EntityId.Should().Be(entityId);
            contentDto.EntityType.Should().Be("documents");
            contentDto.ContentType.Should().Be("Document");
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
            var request = new ChunkSessionRequest(
                entityId,
                "largefile_integration.mp4",
                0, // invalid 
                1024 * 1024 * 100);

            var content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            var response = await _client.PostAsync("/api/v1/content/chunked/init", content);
            response.Should().HaveStatusCode(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task UploadChunk_ValidChunk_ReturnsOkAndStoresChunk()
        {
            // Arrange: first init the chunk session
            Guid entityId = Guid.NewGuid();
            var initReq = new ChunkSessionRequest(
                entityId,
                "largefile_integration_upload_chunk.mp4",
                TotalChunks: 3,
                TotalSize: 3 * 1024 * 1024);

            var initContent = new StringContent(JsonSerializer.Serialize(initReq), Encoding.UTF8, "application/json");
            var initResp = await _client.PostAsync("/api/v1/content/chunked/init", initContent);
            var sessionResponse = await initResp.Content.ReadFromJsonAsync<ChunkSession>();
            var sessionId = sessionResponse.Id;

            int chunkIndex = 0;
            byte[] chunkBytes = Encoding.UTF8.GetBytes("This is chunk data for integration test");
            var chunkContent = CreateFileUploadContent($"chunk-{chunkIndex}", "application/octet-stream", chunkBytes);

            // Act
            var response = await _client.PostAsync($"/api/v1/content/chunked/{sessionId}/{chunkIndex}", chunkContent);

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
            Guid sessionId = Guid.NewGuid(); // Non-existent session
            int chunkIndex = 0;
            byte[] chunkBytes = Encoding.UTF8.GetBytes("This is chunk data");
            var chunkContent = CreateFileUploadContent($"chunk-{chunkIndex}", "application/octet-stream", chunkBytes);

            var response = await _client.PostAsync($"/api/v1/content/chunked/{sessionId}/{chunkIndex}", chunkContent);
            response.Should().HaveStatusCode(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task CompleteChunkSession_ValidSession_ReturnsCreatedContentAndAssemblesFile()
        {
            // 1) init
            Guid entityId = Guid.NewGuid();
            string fileName = "largefile_complete_integration.mp4";
            int totalChunks = 3;
            int totalSize = totalChunks * 1024 * 1024;

            var initReq = new ChunkSessionRequest(entityId, fileName, totalChunks, totalSize);
            var initResp = await _client.PostAsJsonAsync("/api/v1/content/chunked/init", initReq);
            var sessionResponse = await initResp.Content.ReadFromJsonAsync<ChunkSession>();
            Guid sessionId = sessionResponse.Id;

            // 2) upload all chunks
            for (int i = 0; i < totalChunks; i++)
            {
                byte[] chunkBytes = Encoding.UTF8.GetBytes($"Chunk data for integration test - chunk {i}");
                var chunkContent = CreateFileUploadContent($"chunk-{i}", "application/octet-stream", chunkBytes);
                var chunkResp = await _client.PostAsync($"/api/v1/content/chunked/{sessionId}/{i}", chunkContent);
                chunkResp.EnsureSuccessStatusCode();
            }

            // 3) complete
            var response = await _client.PostAsync($"/api/v1/content/chunked/complete/{sessionId}", null);

            // 4) assert
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
