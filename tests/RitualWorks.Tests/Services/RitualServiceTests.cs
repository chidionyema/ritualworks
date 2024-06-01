using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RitualWorks.Db;
using RitualWorks.DTOs;
using RitualWorks.Services;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace RitualWorks.Tests.Services
{
    public class RitualServiceTests
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly BlobContainerClient _blobContainerClient;
        private readonly IConfiguration _configuration;
        private readonly RitualService _ritualService;

        public RitualServiceTests()
        {
            // Set up in-memory database
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: "RitualWorksTestDb")
                .Options;
            _dbContext = new ApplicationDbContext(options);

            // Set up configuration
            var configurationBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "AzureBlobStorage:ConnectionString", "UseDevelopmentStorage=true" },
                    { "AzureBlobStorage:ContainerName", "test-container" }
                });
            _configuration = configurationBuilder.Build();

            // Initialize the BlobServiceClient
            _blobServiceClient = new BlobServiceClient(_configuration["AzureBlobStorage:ConnectionString"]);
            _blobContainerClient = _blobServiceClient.GetBlobContainerClient(_configuration["AzureBlobStorage:ContainerName"]);

            // Ensure the container exists
            _blobContainerClient.CreateIfNotExists();

            // Initialize the service with real instances
            _ritualService = new RitualService(_dbContext, _blobServiceClient, _configuration);
        }

        [Fact]
        public async Task CreateRitualAsync_ShouldReturnRitualDto()
        {
            // Arrange
            var createRitualDto = new CreateRitualDto
            {
                Title = "Test Ritual",
                Description = "Test Description",
                TextContent = "Test Content",
                RitualTypeId = 1
            };
            using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

            // Act
            var result = await _ritualService.CreateRitualAsync(createRitualDto, stream);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Test Ritual", result.Title);
            Assert.Equal("Test Description", result.Description);
            Assert.Equal("Test Content", result.TextContent);
            Assert.Equal(1, result.RitualTypeId);

            // Verify persistence in the database
            var persistedRitual = await _dbContext.Rituals.FindAsync(result.Id);
            Assert.NotNull(persistedRitual);
            Assert.Equal("Test Ritual", persistedRitual.Title);
            Assert.Equal("Test Description", persistedRitual.Description);

            // Verify the media is uploaded
            var blobClient = _blobContainerClient.GetBlobClient(result.Title + "-media");
            Assert.True(await blobClient.ExistsAsync());
        }

        [Fact]
        public async Task GetAllRitualsAsync_ShouldReturnAllRituals()
        {
            // Arrange
            await SeedDatabaseAsync();

            // Act
            var result = await _ritualService.GetAllRitualsAsync();

            // Assert
            Assert.NotEmpty(result);
            var firstRitual = result.FirstOrDefault();
            Assert.NotNull(firstRitual);
            Assert.Equal("Seeded Ritual", firstRitual.Title);
        }

        [Fact]
        public async Task GetRitualByIdAsync_ShouldReturnRitualDto()
        {
            // Arrange
            var createdRitual = await SeedDatabaseAsync();

            // Act
            var result = await _ritualService.GetRitualByIdAsync(createdRitual.Id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(createdRitual.Title, result.Title);
            Assert.Equal(createdRitual.Description, result.Description);
            Assert.Equal(createdRitual.TextContent, result.TextContent);
            Assert.Equal(createdRitual.MediaUrl, result.MediaUrl);
            Assert.Equal(createdRitual.RitualTypeId, result.RitualTypeId);
        }

        [Fact]
        public async Task UpdateRitualAsync_ShouldUpdateRitual()
        {
            // Arrange
            var createdRitual = await SeedDatabaseAsync();
            var updateRitualDto = new CreateRitualDto
            {
                Title = "Updated Ritual",
                Description = "Updated Description",
                TextContent = "Updated Content",
                RitualTypeId = 1
            };
            using var stream = new MemoryStream(new byte[] { 5, 6, 7, 8 });

            // Act
            var result = await _ritualService.UpdateRitualAsync(createdRitual.Id, updateRitualDto, stream);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Updated Ritual", result.Title);
            Assert.Equal("Updated Description", result.Description);
            Assert.Equal("Updated Content", result.TextContent);
            Assert.Equal(1, result.RitualTypeId);

            // Verify persistence in the database
            var updatedRitual = await _dbContext.Rituals.FindAsync(result.Id);
            Assert.NotNull(updatedRitual);
            Assert.Equal("Updated Ritual", updatedRitual.Title);
            Assert.Equal("Updated Description", updatedRitual.Description);
            Assert.Equal("Updated Content", updatedRitual.TextContent);

            // Verify the media is uploaded
            var blobClient = _blobContainerClient.GetBlobClient(result.Title + "-media");
            Assert.True(await blobClient.ExistsAsync());
        }

        [Fact]
        public async Task DeleteRitualAsync_ShouldDeleteRitual()
        {
            // Arrange
            var createdRitual = await SeedDatabaseAsync();

            // Act
            var result = await _ritualService.DeleteRitualAsync(createdRitual.Id);

            // Assert
            Assert.True(result);

            // Verify the ritual is removed from the database
            var deletedRitual = await _dbContext.Rituals.FindAsync(createdRitual.Id);
            Assert.Null(deletedRitual);
        }

        private async Task<Ritual> SeedDatabaseAsync()
        {
            var ritual = new Ritual
            {
                Title = "Seeded Ritual",
                Description = "Seeded Description",
                TextContent = "Seeded Content",
                MediaUrl = "http://example.com/media",
                RitualTypeId = 1
            };

            _dbContext.Rituals.Add(ritual);
            await _dbContext.SaveChangesAsync();

            return ritual;
        }
    }
}
