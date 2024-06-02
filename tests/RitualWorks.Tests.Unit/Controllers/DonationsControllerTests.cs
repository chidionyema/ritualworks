using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Threading.Tasks;
using RitualWorks.Contracts;
using RitualWorks.DTOs;
using RitualWorks.Controllers;
using Xunit;
using System.Collections.Generic;

namespace RitualWorks.Tests.Unit
{
    public class DonationsControllerTests
    {
        private readonly Mock<IDonationService> _donationServiceMock;
        private readonly DonationsController _controller;

        public DonationsControllerTests()
        {
            _donationServiceMock = new Mock<IDonationService>();
            _controller = new DonationsController(_donationServiceMock.Object);
        }

        [Fact]
        public async Task CreateDonation_WithValidDto_ReturnsCreatedAtAction()
        {
            // Arrange
            var createDonationDto = new CreateDonationDto
            {
                RitualId = 1,
                Amount = 100,
             //   DonorName = "John Doe"
            };
            var createdDonation = new DonationDto
            {
                Id = 1,
                RitualId = 1,
                Amount = 100,
                DonorName = "John Doe"
            };

            _donationServiceMock.Setup(service => service.CreateDonationAsync(createDonationDto))
                .ReturnsAsync(createdDonation);

            // Act
            var result = await _controller.CreateDonation(createDonationDto);

            // Assert
            var createdAtActionResult = Assert.IsType<CreatedAtActionResult>(result);
            Assert.Equal(nameof(_controller.GetDonationById), createdAtActionResult.ActionName);
            Assert.Equal(createdDonation.Id, createdAtActionResult.RouteValues["id"]);
            Assert.Equal(createdDonation, createdAtActionResult.Value);
        }

        [Fact]
        public async Task GetDonationById_ExistingId_ReturnsOkObjectResult()
        {
            // Arrange
            var donationId = 1;
            var donation = new DonationDto
            {
                Id = donationId,
                RitualId = 1,
                Amount = 100,
                DonorName = "John Doe"
            };

            _donationServiceMock.Setup(service => service.GetDonationByIdAsync(donationId))
                .ReturnsAsync(donation);

            // Act
            var result = await _controller.GetDonationById(donationId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(donation, okResult.Value);
        }

        [Fact]
        public async Task GetDonationById_NonExistingId_ReturnsNotFoundResult()
        {
            // Arrange
            var donationId = 1;

            _donationServiceMock.Setup(service => service.GetDonationByIdAsync(donationId))
                .ReturnsAsync((DonationDto)null);

            // Act
            var result = await _controller.GetDonationById(donationId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task GetDonationsByRitualId_ExistingRitualId_ReturnsOkObjectResult()
        {
            // Arrange
            var ritualId = 1;
            var donations = new List<DonationDto>
            {
                new DonationDto { Id = 1, RitualId = ritualId, Amount = 100, DonorName = "John Doe" },
                new DonationDto { Id = 2, RitualId = ritualId, Amount = 200, DonorName = "Jane Doe" }
            };

            _donationServiceMock.Setup(service => service.GetDonationsByRitualIdAsync(ritualId))
                .ReturnsAsync(donations);

            // Act
            var result = await _controller.GetDonationsByRitualId(ritualId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(donations, okResult.Value);
        }
    }
}
