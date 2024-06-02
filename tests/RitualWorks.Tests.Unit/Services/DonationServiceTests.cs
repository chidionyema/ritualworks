using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using RitualWorks.Contracts;
using RitualWorks.DTOs;
using RitualWorks.Db;
using RitualWorks.Services;
using Xunit;

namespace RitualWorks.Tests.Services
{
    public class DonationServiceTests
    {
        private readonly Mock<IDonationRepository> _donationRepositoryMock;
        private readonly Mock<IRitualService> _ritualServiceMock;
        private readonly DonationService _donationService;

        public DonationServiceTests()
        {
            _donationRepositoryMock = new Mock<IDonationRepository>();
            _ritualServiceMock = new Mock<IRitualService>();
            _donationService = new DonationService(_donationRepositoryMock.Object, _ritualServiceMock.Object);
        }

        [Fact]
        public async Task CreateDonationAsync_ShouldThrowArgumentNullException_WhenDtoIsNull()
        {
            // Arrange
            CreateDonationDto createDonationDto = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => _donationService.CreateDonationAsync(createDonationDto));
        }

        [Fact]
        public async Task CreateDonationAsync_ShouldReturnDonationDto_WhenValidDtoProvided()
        {
            // Arrange
            var createDonationDto = new CreateDonationDto
            {
                Amount = 100.0m,
                PetitionId = 1,
                RitualId = 1,
               // UserId = "user1"
            };

            var donation = new Donation
            {
                Id = 1,
                Amount = createDonationDto.Amount,
                PetitionId = createDonationDto.PetitionId,
                RitualId = createDonationDto.RitualId,
                UserId = createDonationDto.UserId,
                Created = DateTime.UtcNow
            };

            _donationRepositoryMock.Setup(repo => repo.CreateDonationAsync(It.IsAny<Donation>()))
                .ReturnsAsync(donation);

            var ritual = new RitualDto
            {
                Id = 1,
                TokenAmount = 50.0m,
                IsLocked = false
            };

            _ritualServiceMock.Setup(service => service.GetRitualByIdAsync(createDonationDto.RitualId.Value))
                .ReturnsAsync(ritual);

            // Act
            var result = await _donationService.CreateDonationAsync(createDonationDto);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(donation.Id, result.Id);
            Assert.Equal(donation.Amount, result.Amount);
            Assert.Equal(donation.PetitionId, result.PetitionId);
            Assert.Equal(donation.RitualId, result.RitualId);
            Assert.Equal(donation.UserId, result.UserId);

            // Verify Locking
            _ritualServiceMock.Verify(service => service.LockRitualAsync(createDonationDto.RitualId.Value), Times.Once);
        }

        [Fact]
        public async Task GetDonationsByPetitionIdAsync_ShouldReturnDonations_WhenDonationsExist()
        {
            // Arrange
            var petitionId = 1;
            var donations = new List<Donation>
            {
                new Donation { Id = 1, Amount = 100.0m, PetitionId = petitionId, RitualId = 1, UserId = "user1" },
                new Donation { Id = 2, Amount = 50.0m, PetitionId = petitionId, RitualId = 2, UserId = "user2" }
            };

            _donationRepositoryMock.Setup(repo => repo.GetDonationsByPetitionIdAsync(petitionId))
                .ReturnsAsync(donations);

            // Act
            var result = await _donationService.GetDonationsByPetitionIdAsync(petitionId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(donations.Count, result.Count());

            // Verify each donation
            var resultList = result.ToList();
            for (int i = 0; i < donations.Count; i++)
            {
                Assert.Equal(donations[i].Id, resultList[i].Id);
                Assert.Equal(donations[i].Amount, resultList[i].Amount);
                Assert.Equal(donations[i].PetitionId, resultList[i].PetitionId);
                Assert.Equal(donations[i].RitualId, resultList[i].RitualId);
                Assert.Equal(donations[i].UserId, resultList[i].UserId);
            }
        }

        [Fact]
        public async Task GetDonationsByRitualIdAsync_ShouldReturnDonations_WhenDonationsExist()
        {
            // Arrange
            var ritualId = 1;
            var donations = new List<Donation>
            {
                new Donation { Id = 1, Amount = 100.0m, PetitionId = 1, RitualId = ritualId, UserId = "user1" },
                new Donation { Id = 2, Amount = 50.0m, PetitionId = 2, RitualId = ritualId, UserId = "user2" }
            };

            _donationRepositoryMock.Setup(repo => repo.GetDonationsByRitualIdAsync(ritualId))
                .ReturnsAsync(donations);

            // Act
            var result = await _donationService.GetDonationsByRitualIdAsync(ritualId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(donations.Count, result.Count());

            // Verify each donation
            var resultList = result.ToList();
            for (int i = 0; i < donations.Count; i++)
            {
                Assert.Equal(donations[i].Id, resultList[i].Id);
                Assert.Equal(donations[i].Amount, resultList[i].Amount);
                Assert.Equal(donations[i].PetitionId, resultList[i].PetitionId);
                Assert.Equal(donations[i].RitualId, resultList[i].RitualId);
                Assert.Equal(donations[i].UserId, resultList[i].UserId);
            }
        }

        [Fact]
        public async Task GetDonationByIdAsync_ShouldReturnDonation_WhenDonationExists()
        {
            // Arrange
            var donationId = 1;
            var donation = new Donation
            {
                Id = donationId,
                Amount = 100.0m,
                PetitionId = 1,
                RitualId = 1,
                UserId = "user1"
            };

            _donationRepositoryMock.Setup(repo => repo.GetDonationByIdAsync(donationId))
                .ReturnsAsync(donation);

            // Act
            var result = await _donationService.GetDonationByIdAsync(donationId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(donation.Id, result.Id);
            Assert.Equal(donation.Amount, result.Amount);
            Assert.Equal(donation.PetitionId, result.PetitionId);
            Assert.Equal(donation.RitualId, result.RitualId);
            Assert.Equal(donation.UserId, result.UserId);
        }

        [Fact]
        public async Task GetDonationByIdAsync_ShouldReturnNull_WhenDonationDoesNotExist()
        {
            // Arrange
            var donationId = 1;
            _donationRepositoryMock.Setup(repo => repo.GetDonationByIdAsync(donationId))
                .ReturnsAsync((Donation)null);

            // Act
            var result = await _donationService.GetDonationByIdAsync(donationId);

            // Assert
            Assert.Null(result);
        }
    }
}
