using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RitualWorks.Contracts;
using RitualWorks.Db;
using RitualWorks.DTOs;
using RitualWorks.Repositories;
using RitualWorks.Services;
using RitualWorks.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace RitualWorks.Tests.Services
{
    public class RitualServiceTests
    {
        private readonly RitualWorksContext _dbContext;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly BlobContainerClient _blobContainerClient;
        private readonly IRitualRepository _ritualRepository;
        private readonly RitualService _ritualService;
        private readonly string _containerName;

        public RitualServiceTests()
        {
            // Set up SQLite in-memory database
            var options = new DbContextOptionsBuilder<RitualWorksContext>()
                .UseSqlite("Filename=:memory:")
                .Options;
            _dbContext = new RitualWorksContext(options);

            _dbContext.Database.OpenConnection();
            _dbContext.Database.EnsureCreated();

            // Set up configuration
            var configurationBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "AzureBlobStorage:ConnectionString", "UseDevelopmentStorage=true" },
                    { "AzureBlobStorage:ContainerName", "test-container" }
                });
            var configuration = configurationBuilder.Build();
            _containerName = configuration["AzureBlobStorage:ContainerName"];

            // Initialize the BlobServiceClient
            _blobServiceClient = new BlobServiceClient(configuration["AzureBlobStorage:ConnectionString"]);
            _blobContainerClient = _blobServiceClient.GetBlobContainerClient(_containerName);

            // Ensure the container exists
            _blobContainerClient.CreateIfNotExists();

            // Initialize the repository
            _ritualRepository = new RitualRepository(_dbContext);

            // Initialize the service with real instances
            _ritualService = new RitualService(_ritualRepository, _blobServiceClient, Options.Create(new BlobSettings { ContainerName = _containerName }));
        }

        [Fact]
        public async Task CreateRitualAsync_ShouldReturnRitualDto()
        {
            // Arrange
            var createRitualDto = new CreateRitualDto
            {
                Title = "Test Ritual",
                Description = "Test Description",
                Preview = "Test Preview",
                FullContent = "Test Content",
                ExternalLink = "http://example.com",
                TokenAmount = 10.0m,
                RitualType = RitualTypeEnum.Ceremonial
            };
            using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

            // Act
            RitualDto result = await _ritualService.CreateRitualAsync(createRitualDto, stream);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Test Ritual", result.Title);
            Assert.Equal("Test Description", result.Description);
            Assert.Equal("Test Preview", result.Preview);
            Assert.Equal("Test Content", result.FullContent);
            Assert.Equal("http://example.com", result.ExternalLink);
            Assert.Equal(10.0m, result.TokenAmount);
            Assert.Equal(RitualTypeEnum.Ceremonial, result.RitualType);

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
        public async Task UpdateRitualAsync_ShouldUpdateRitual()
        {
            // Arrange
            var createdRitual = await SeedDatabaseAsync();
            var updateRitualDto = new CreateRitualDto
            {
                Title = "Updated Ritual",
                Description = "Updated Description",
                Preview = "Updated Preview",
                FullContent = "Updated Content",
                ExternalLink = "http://updated.com",
                TokenAmount = 20.0m,
                RitualType = RitualTypeEnum.Meditation
            };
            using var stream = new MemoryStream(new byte[] { 5, 6, 7, 8 });

            // Act
            RitualDto result = await _ritualService.UpdateRitualAsync(createdRitual.Id, updateRitualDto, stream);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Updated Ritual", result.Title);
            Assert.Equal("Updated Description", result.Description);
            Assert.Equal("Updated Preview", result.Preview);
            Assert.Equal("Updated Content", result.FullContent);
            Assert.Equal("http://updated.com", result.ExternalLink);
            Assert.Equal(20.0m, result.TokenAmount);
            Assert.Equal(RitualTypeEnum.Meditation, result.RitualType);

            // Verify persistence in the database
            var updatedRitual = await _dbContext.Rituals.FindAsync(result.Id);
            Assert.NotNull(updatedRitual);
            Assert.Equal("Updated Ritual", updatedRitual.Title);
            Assert.Equal("Updated Description", updatedRitual.Description);
            Assert.Equal("Updated Preview", updatedRitual.Preview);
            Assert.Equal("Updated Content", updatedRitual.FullContent);

            // Verify the media is uploaded
            var blobClient = _blobContainerClient.GetBlobClient(result.Title + "-media");
            Assert.True(await blobClient.ExistsAsync());
        }

        [Fact]
        public async Task UpdateRitualAsync_ShouldNotUpdateLockedRitual()
        {
            // Arrange
            var createdRitual = await SeedDatabaseAsync(lockRitual: true);
            var updateRitualDto = new CreateRitualDto
            {
                Title = "Updated Ritual",
                Description = "Updated Description",
                Preview = "Updated Preview",
                FullContent = "Updated Content",
                ExternalLink = "http://updated.com",
                TokenAmount = 20.0m,
                RitualType = RitualTypeEnum.Meditation
            };
            using var stream = new MemoryStream(new byte[] { 5, 6, 7, 8 });

            // Act
            RitualDto result = await _ritualService.UpdateRitualAsync(createdRitual.Id, updateRitualDto, stream);

            // Assert
            Assert.Null(result);

            // Verify persistence in the database
            var lockedRitual = await _dbContext.Rituals.FindAsync(createdRitual.Id);
            Assert.NotNull(lockedRitual);
            Assert.Equal("Seeded Ritual", lockedRitual.Title); // Ensure original values are retained
            Assert.Equal("Seeded Description", lockedRitual.Description);
        }

        [Fact]
        public async Task GetRitualByIdAsync_ShouldReturnRitualDto()
        {
            // Arrange
            var createdRitual = await SeedDatabaseAsync();

            // Act
            RitualDto result = await _ritualService.GetRitualByIdAsync(createdRitual.Id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(createdRitual.Title, result.Title);
            Assert.Equal(createdRitual.Description, result.Description);
            Assert.Equal(createdRitual.Preview, result.Preview);
            Assert.Equal(createdRitual.FullContent, result.FullContent);
            Assert.Equal(createdRitual.ExternalLink, result.ExternalLink);
            Assert.Equal(createdRitual.TokenAmount, result.TokenAmount);
            Assert.Equal(createdRitual.RitualType, result.RitualType);
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
        public async Task LockRitualAsync_ShouldLockRitual()
        {
            // Arrange
            var createdRitual = await SeedDatabaseAsync();

            // Act
            var success = await _ritualService.LockRitualAsync(createdRitual.Id);

            // Assert
            Assert.True(success);
            var lockedRitual = await _dbContext.Rituals.FindAsync(createdRitual.Id);
            Assert.NotNull(lockedRitual);
            Assert.True(lockedRitual.IsLocked);
        }

        [Fact]
        public async Task RateRitualAsync_ShouldRateRitual()
        {
            // Arrange
            var createdRitual = await SeedDatabaseAsync();
            var rating = 4.5;

            // Act
            var success = await _ritualService.RateRitualAsync(createdRitual.Id, rating);

            // Assert
            Assert.True(success);
            var ratedRitual = await _dbContext.Rituals.FindAsync(createdRitual.Id);
            Assert.NotNull(ratedRitual);
            Assert.Equal(rating, ratedRitual.Rating);
        }

        [Fact]
        public async Task SearchRitualsAsync_ShouldReturnMatchingRituals()
        {
            // Arrange
            await SeedDatabaseAsync();

            // Act
            var result = await _ritualService.SearchRitualsAsync("Seeded", RitualTypeEnum.Ceremonial);

            // Assert
            Assert.NotEmpty(result);
            var matchingRitual = result.FirstOrDefault();
            Assert.NotNull(matchingRitual);
            Assert.Equal("Seeded Ritual", matchingRitual.Title);
        }

        private async Task<Ritual> SeedDatabaseAsync(bool lockRitual = false)
        {
            var ritual = new Ritual
            {
                Title = "Seeded Ritual",
                Description = "Seeded Description",
                Preview = "Seeded Preview",
                FullContent = "Seeded Content",
                ExternalLink = "http://example.com/media",
                TokenAmount = 15.0m,
                RitualType = RitualTypeEnum.Ceremonial,
                IsLocked = lockRitual
            };

            _dbContext.Rituals.Add(ritual);
            await _dbContext.SaveChangesAsync();

            return ritual;
        }
    }
}
