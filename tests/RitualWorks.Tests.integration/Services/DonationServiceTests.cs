using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using RitualWorks.Contracts;
using RitualWorks.Db;
using RitualWorks.Controllers;
using RitualWorks.Repositories;
using RitualWorks.Services;
using Xunit;
using Microsoft.Extensions.Caching.Memory;

namespace RitualWorks.Tests.Services
{
    public class DonationServiceIntegrationTests
    {
        private readonly RitualWorksContext _dbContext;
        private readonly IDonationRepository _donationRepository;
        private readonly IRitualService _ritualService;
        private readonly DonationService _donationService;
        private readonly Mock<IMemoryCache> _memoryCacheMock;

        public DonationServiceIntegrationTests()
        {
            // Set up SQLite in-memory database
            var options = new DbContextOptionsBuilder<RitualWorksContext>()
                .UseSqlite("Filename=:memory:")
                .Options;
            _dbContext = new RitualWorksContext(options);

            _dbContext.Database.OpenConnection();
            _dbContext.Database.EnsureCreated();

            _memoryCacheMock = new Mock<IMemoryCache>();
            _memoryCacheMock.Setup(cache => cache.CreateEntry(It.IsAny<object>())).Returns(Mock.Of<ICacheEntry>());

            // Initialize the repository and services
            _donationRepository = new DonationRepository(_dbContext);
            var ritualRepository = new RitualRepository(_dbContext, _memoryCacheMock.Object);
            _ritualService = new RitualService(ritualRepository);
            _donationService = new DonationService(_donationRepository, _ritualService, _dbContext);
        }

        [Fact]
        public async Task CreateDonationAsync_ShouldReturnDonationDto()
        {
            // Arrange
            await SeedDatabaseAsync(); // Ensure related data is seeded
            var createDonationDto = new CreateDonationDto
            {
                Amount = 100,
                PetitionId = 1,
                RitualId = 1,
                UserId = "user1"
            };

            // Act
            var (result, sessionId) = await _donationService.CreateDonationAsync(createDonationDto, "http://example.com");

            // Assert
            Assert.NotNull(result);
            Assert.Equal(100, result.Amount);
            Assert.Equal(1, result.PetitionId);
            Assert.Equal(1, result.RitualId);
            Assert.Equal("user1", result.UserId);

            // Verify persistence in the database
            var persistedDonation = await _dbContext.Donations.FindAsync(result.Id);
            Assert.NotNull(persistedDonation);
            Assert.Equal(100, persistedDonation.Amount);
            Assert.Equal(1, persistedDonation.PetitionId);
            Assert.Equal(1, persistedDonation.RitualId);
            Assert.Equal("user1", persistedDonation.UserId);
        }

        [Fact]
        public async Task GetDonationsByPetitionIdAsync_ShouldReturnDonations()
        {
            // Arrange
            await SeedDatabaseAsync();

            // Act
            var result = await _donationService.GetDonationsByPetitionIdAsync(1);

            // Assert
            Assert.NotEmpty(result);
            var firstDonation = result.FirstOrDefault();
            Assert.NotNull(firstDonation);
            Assert.Equal(1, firstDonation.PetitionId);
        }

        [Fact]
        public async Task GetDonationsByRitualIdAsync_ShouldReturnDonations()
        {
            // Arrange
            await SeedDatabaseAsync();

            // Act
            var result = await _donationService.GetDonationsByRitualIdAsync(1);

            // Assert
            Assert.NotEmpty(result);
            var firstDonation = result.FirstOrDefault();
            Assert.NotNull(firstDonation);
            Assert.Equal(1, firstDonation.RitualId);
        }

        [Fact]
        public async Task GetDonationByIdAsync_ShouldReturnDonationDto()
        {
            // Arrange
            var createdDonation = await SeedDatabaseAsync();

            // Act
            var result = await _donationService.GetDonationByIdAsync(createdDonation.Id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(createdDonation.Id, result.Id);
            Assert.Equal(createdDonation.Amount, result.Amount);
            Assert.Equal(createdDonation.PetitionId, result.PetitionId);
            Assert.Equal(createdDonation.RitualId, result.RitualId);
            Assert.Equal(createdDonation.UserId, result.UserId);
        }

        private async Task<Donation> SeedDatabaseAsync()
        {
            // Seed a ritual
            var ritual = new Ritual
            {
                Id = 1,
                Title = "Test Ritual",
                TokenAmount = 50.0m,
                IsLocked = false
            };
            if (!_dbContext.Rituals.Any(r => r.Id == ritual.Id))
            {
                _dbContext.Rituals.Add(ritual);
                await _dbContext.SaveChangesAsync();
            }

            // Seed a petition
            var petition = new Petition
            {
                Id = 1,
                RitualId = 1,
                Description = "Test Petition",
                UserId = "user1",
                Created = DateTime.UtcNow
            };
            if (!_dbContext.Petitions.Any(p => p.Id == petition.Id))
            {
                _dbContext.Petitions.Add(petition);
                await _dbContext.SaveChangesAsync();
            }

            // Seed a donation
            var donation = new Donation
            {
                Amount = 100,
                PetitionId = 1,
                RitualId = 1,
                UserId = "user1",
                Created = DateTime.UtcNow
            };

            _dbContext.Donations.Add(donation);
            await _dbContext.SaveChangesAsync();

            return donation;
        }
    }
}
