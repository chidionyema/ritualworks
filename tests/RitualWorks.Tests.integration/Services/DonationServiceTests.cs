using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RitualWorks.Contracts;
using RitualWorks.Db;
using RitualWorks.DTOs;
using RitualWorks.Repositories;
using RitualWorks.Services;
using RitualWorks.Settings;
using System;
using System.Linq;
using System.Threading.Tasks;


namespace RitualWorks.Tests.Services
{
    public class DonationServiceTests
    {
        private readonly RitualWorksContext _dbContext;
        private readonly IDonationRepository _donationRepository;
        private readonly IRitualService _ritualService;
        private readonly DonationService _donationService;

        public DonationServiceTests()
        {
            // Set up SQLite in-memory database
            var options = new DbContextOptionsBuilder<RitualWorksContext>()
                .UseSqlite("Filename=:memory:")
                .Options;
            _dbContext = new RitualWorksContext(options);

            _dbContext.Database.OpenConnection();
            _dbContext.Database.EnsureCreated();

            // Initialize the repository and services
            _donationRepository = new DonationRepository(_dbContext);
            _ritualService = new RitualService(new RitualRepository(_dbContext), new BlobServiceClient("UseDevelopmentStorage=true"), Options.Create(new BlobSettings { ContainerName = "test-container" }));

            // Initialize the service with real instances
            _donationService = new DonationService(_donationRepository, _ritualService);
        }

        [Fact]
        public async Task CreateDonationAsync_ShouldReturnDonationDto()
        {
            // Arrange
            var createDonationDto = new CreateDonationDto
            {
                Amount = 100,
                PetitionId = 1,
                RitualId = 1,
               // UserId = "user1"
            };

            // Act
            var result = await _donationService.CreateDonationAsync(createDonationDto);

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
