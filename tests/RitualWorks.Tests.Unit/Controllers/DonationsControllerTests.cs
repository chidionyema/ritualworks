using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using RitualWorks.Contracts;
using RitualWorks.Controllers;
using RitualWorks.Services;
using Xunit;

namespace RitualWorks.Tests.Unit.Controllers
{
    public class DonationsControllerTests
    {
        private readonly Mock<IDonationService> _donationServiceMock;
        private readonly DonationsController _controller;

        public DonationsControllerTests()
        {
            _donationServiceMock = new Mock<IDonationService>();
            _controller = new DonationsController(_donationServiceMock.Object);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "http";
            httpContext.Request.Host = new HostString("localhost", 5000);
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
        }

        [Fact]
        public async Task CreateDonation_WithValidDto_ReturnsCreatedAtAction()
        {
            // Arrange
            var createDonationDto = new CreateDonationDto
            {
                Amount = 100,
                PetitionId = 1,
                RitualId = 1,
               // UserId = "user1"
            };

            var createdDonation = new DonationDto
            {
                Id = 1,
                Amount = 100,
                PetitionId = 1,
                RitualId = 1,
               // UserId = "user1"
            };

            _donationServiceMock.Setup(service => service.CreateDonationAsync(createDonationDto, It.IsAny<string>()))
                .ReturnsAsync((createdDonation, "test-session-id"));

            // Act
            var result = await _controller.CreateDonation(createDonationDto);

            // Assert
            var createdAtActionResult = Assert.IsType<CreatedAtActionResult>(result);
            var returnValue = Assert.IsType<DonationDto>(createdAtActionResult.Value);
            Assert.Equal(createdDonation.Id, returnValue.Id);
            Assert.Equal(createdDonation.Amount, returnValue.Amount);
            Assert.Equal(createdDonation.PetitionId, returnValue.PetitionId);
            Assert.Equal(createdDonation.RitualId, returnValue.RitualId);
            Assert.Equal(createdDonation.UserId, returnValue.UserId);
        }
    }
}
