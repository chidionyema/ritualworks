using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using RitualWorks.Contracts;
using RitualWorks.Db;
using RitualWorks.Controllers;
using RitualWorks.Repositories;
using RitualWorks.Services;
using Xunit;

namespace RitualWorks.Tests.Services
{
    public class RitualServiceTests
    {
        private readonly RitualWorksContext _dbContext;
        private readonly IRitualRepository _ritualRepository;
        private readonly RitualService _ritualService;
        private readonly Mock<IMemoryCache> _memoryCacheMock;

        public RitualServiceTests()
        {
            // Set up SQLite in-memory database
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<RitualWorksContext>()
                .UseSqlite(connection)
                .Options;
            _dbContext = new RitualWorksContext(options);

            _dbContext.Database.EnsureCreated();

            // Set up mock cache
            _memoryCacheMock = new Mock<IMemoryCache>();
            _memoryCacheMock.Setup(cache => cache.CreateEntry(It.IsAny<object>())).Returns(Mock.Of<ICacheEntry>());

            // Initialize the repository
            _ritualRepository = new RitualRepository(_dbContext, _memoryCacheMock.Object);

            // Initialize the service with real instances
            _ritualService = new RitualService(_ritualRepository);
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
                TokenAmount = 10.0m,
                RitualType = RitualTypeEnum.Ceremonial,
                MediaUrl = "http://example.com/media"
            };

            // Act
            RitualDto result = await _ritualService.CreateRitualAsync(createRitualDto);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Test Ritual", result.Title);
            Assert.Equal("Test Description", result.Description);
            Assert.Equal("Test Preview", result.Preview);
            Assert.Equal("Test Content", result.FullTextContent);
            Assert.Equal("http://example.com/media", result.MediaUrl);
            Assert.Equal(10.0m, result.TokenAmount);
            Assert.Equal(RitualTypeEnum.Ceremonial, result.RitualType);

            // Verify persistence in the database
            var persistedRitual = await _dbContext.Rituals.FindAsync(result.Id);
            Assert.NotNull(persistedRitual);
            Assert.Equal("Test Ritual", persistedRitual.Title);
            Assert.Equal("Test Description", persistedRitual.Description);

            // Verify cache invalidation
            _memoryCacheMock.Verify(cache => cache.Remove("AllRituals"), Times.Once);
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
                TokenAmount = 20.0m,
                RitualType = RitualTypeEnum.Meditation,
                MediaUrl = "http://updated.com/media"
            };

            // Act
            RitualDto? result = await _ritualService.UpdateRitualAsync(createdRitual.Id, updateRitualDto);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Updated Ritual", result.Title);
            Assert.Equal("Updated Description", result.Description);
            Assert.Equal("Updated Preview", result.Preview);
            Assert.Equal("Updated Content", result.FullTextContent);
            Assert.Equal("http://updated.com/media", result.MediaUrl);
            Assert.Equal(20.0m, result.TokenAmount);
            Assert.Equal(RitualTypeEnum.Meditation, result.RitualType);

            // Verify persistence in the database
            var updatedRitual = await _dbContext.Rituals.FindAsync(result.Id);
            Assert.NotNull(updatedRitual);
            Assert.Equal("Updated Ritual", updatedRitual.Title);
            Assert.Equal("Updated Description", updatedRitual.Description);
            Assert.Equal("Updated Preview", updatedRitual.Preview);
            Assert.Equal("Updated Content", updatedRitual.FullTextContent);

            // Verify cache invalidation
            _memoryCacheMock.Verify(cache => cache.Remove("AllRituals"), Times.Once);
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
                TokenAmount = 20.0m,
                RitualType = RitualTypeEnum.Meditation,
                MediaUrl = "http://updated.com/media"
            };

            // Act
            RitualDto? result = await _ritualService.UpdateRitualAsync(createdRitual.Id, updateRitualDto);

            // Assert
            Assert.Null(result);

            // Verify persistence in the database
            var lockedRitual = await _dbContext.Rituals.FindAsync(createdRitual.Id);
            Assert.NotNull(lockedRitual);
            Assert.Equal("Seeded Ritual", lockedRitual.Title); // Ensure original values are retained
            Assert.Equal("Seeded Description", lockedRitual.Description);

            // Verify cache invalidation does not occur
            _memoryCacheMock.Verify(cache => cache.Remove("AllRituals"), Times.Never);
        }

        [Fact]
        public async Task GetRitualByIdAsync_ShouldReturnRitualDto()
        {
            // Arrange
            var createdRitual = await SeedDatabaseAsync();

            // Act
            RitualDto? result = await _ritualService.GetRitualByIdAsync(createdRitual.Id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(createdRitual.Title, result.Title);
            Assert.Equal(createdRitual.Description, result.Description);
            Assert.Equal(createdRitual.Preview, result.Preview);
            Assert.Equal(createdRitual.FullTextContent, result.FullTextContent);
            Assert.Equal(createdRitual.MediaUrl, result.MediaUrl);
            Assert.Equal(createdRitual.TokenAmount, result.TokenAmount);
            Assert.Equal(createdRitual.RitualType, result.RitualType);
        }

        [Fact]
        public async Task GetAllRitualsAsync_ShouldReturnAllRituals()
        {
            // Arrange
            await SeedDatabaseAsync();

            // Set up cache
            var cacheEntry = new Mock<ICacheEntry>();
            _memoryCacheMock.Setup(cache => cache.CreateEntry(It.IsAny<object>())).Returns(cacheEntry.Object);

            // Act
            var result = await _ritualService.GetAllRitualsAsync();

            // Assert
            Assert.NotEmpty(result);
            var firstRitual = result.FirstOrDefault();
            Assert.NotNull(firstRitual);
            Assert.Equal("Seeded Ritual", firstRitual.Title);

            // Verify cache was used
            _memoryCacheMock.Verify(cache => cache.CreateEntry(It.IsAny<object>()), Times.Once);
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

            // Verify cache invalidation
            _memoryCacheMock.Verify(cache => cache.Remove("AllRituals"), Times.Once);
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

            // Verify cache invalidation
            _memoryCacheMock.Verify(cache => cache.Remove("AllRituals"), Times.Once);
        }

        [Fact]
        public async Task SearchRitualsAsync_ShouldReturnMatchingRituals()
        {
            // Arrange
            await SeedDatabaseAsync();

            // Set up cache
            var cacheEntry = new Mock<ICacheEntry>();
            _memoryCacheMock.Setup(cache => cache.CreateEntry(It.IsAny<object>())).Returns(cacheEntry.Object);

            // Act
            var result = await _ritualService.SearchRitualsAsync("Seeded", RitualTypeEnum.Ceremonial);

            // Assert
            Assert.NotEmpty(result);
            var matchingRitual = result.FirstOrDefault();
            Assert.NotNull(matchingRitual);
            Assert.Equal("Seeded Ritual", matchingRitual.Title);

            // Verify cache was used
            _memoryCacheMock.Verify(cache => cache.CreateEntry(It.IsAny<object>()), Times.Once);
        }

        private async Task<Ritual> SeedDatabaseAsync(bool lockRitual = false)
        {
            var ritual = new Ritual
            {
                Title = "Seeded Ritual",
                Description = "Seeded Description",
                Preview = "Seeded Preview",
                FullTextContent = "Seeded Content",
                TokenAmount = 15.0m,
                RitualType = RitualTypeEnum.Ceremonial,
                MediaUrl = "http://example.com/media",
                IsLocked = lockRitual
            };

            _dbContext.Rituals.Add(ritual);
            await _dbContext.SaveChangesAsync();

            return ritual;
        }
    }
}
