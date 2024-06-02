using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Threading.Tasks;
using RitualWorks.DTOs;
using RitualWorks.Services;
using RitualWorks.Controllers;
using Xunit;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Collections.Generic;

namespace RitualWorks.Tests.Unit
{
    public class RitualsControllerTests
    {
        private readonly Mock<IRitualService> _ritualServiceMock;
        private readonly RitualsController _controller;

        public RitualsControllerTests()
        {
            _ritualServiceMock = new Mock<IRitualService>();
            _controller = new RitualsController(_ritualServiceMock.Object);
        }

        [Fact]
        public async Task CreateRitual_WithValidDtoAndMediaFile_ReturnsOk()
        {
            // Arrange
            var createRitualDto = new CreateRitualDto
            {
                Title = "Sample Ritual",
                Description = "Sample Description"
            };

            var mediaFileMock = new Mock<IFormFile>();
            var stream = new MemoryStream();
            mediaFileMock.Setup(_ => _.OpenReadStream()).Returns(stream);

            var createdRitual = new RitualDto
            {
                Id = 1,
                Title = "Sample Ritual",
                Description = "Sample Description"
            };

            _ritualServiceMock.Setup(service => service.CreateRitualAsync(createRitualDto, stream))
                .ReturnsAsync(createdRitual);

            // Act
            var result = await _controller.CreateRitual(createRitualDto, mediaFileMock.Object);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(createdRitual, okResult.Value);
        }

        [Fact]
        public async Task CreateRitual_WithNullMediaFile_ReturnsBadRequest()
        {
            // Arrange
            var createRitualDto = new CreateRitualDto
            {
                Title = "Sample Ritual",
                Description = "Sample Description"
            };

            // Act
            var result = await _controller.CreateRitual(createRitualDto, null);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Media file cannot be null", badRequestResult.Value);
        }

        [Fact]
        public async Task UpdateRitual_WithValidIdAndDto_ReturnsOk()
        {
            // Arrange
            var updateRitualDto = new CreateRitualDto
            {
                Title = "Updated Ritual",
                Description = "Updated Description"
            };

            var mediaFileMock = new Mock<IFormFile>();
            var stream = new MemoryStream();
            mediaFileMock.Setup(_ => _.OpenReadStream()).Returns(stream);

            var updatedRitual = new RitualDto
            {
                Id = 1,
                Title = "Updated Ritual",
                Description = "Updated Description"
            };

            _ritualServiceMock.Setup(service => service.UpdateRitualAsync(1, updateRitualDto, stream))
                .ReturnsAsync(updatedRitual);

            // Act
            var result = await _controller.UpdateRitual(1, updateRitualDto, mediaFileMock.Object);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(updatedRitual, okResult.Value);
        }

        [Fact]
        public async Task UpdateRitual_WithNonExistingId_ReturnsNotFound()
        {
            // Arrange
            var updateRitualDto = new CreateRitualDto
            {
                Title = "Updated Ritual",
                Description = "Updated Description"
            };

            _ritualServiceMock.Setup(service => service.UpdateRitualAsync(1, updateRitualDto, null))
                .ReturnsAsync((RitualDto)null);

            // Act
            var result = await _controller.UpdateRitual(1, updateRitualDto, null);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task GetRitualById_ExistingId_ReturnsOkObjectResult()
        {
            // Arrange
            var ritualId = 1;
            var ritual = new RitualDto
            {
                Id = ritualId,
                Title = "Sample Ritual",
                Description = "Sample Description"
            };

            _ritualServiceMock.Setup(service => service.GetRitualByIdAsync(ritualId))
                .ReturnsAsync(ritual);

            // Act
            var result = await _controller.GetRitualById(ritualId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(ritual, okResult.Value);
        }

        [Fact]
        public async Task GetRitualById_NonExistingId_ReturnsNotFoundResult()
        {
            // Arrange
            var ritualId = 1;

            _ritualServiceMock.Setup(service => service.GetRitualByIdAsync(ritualId))
                .ReturnsAsync((RitualDto)null);

            // Act
            var result = await _controller.GetRitualById(ritualId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task GetAllRituals_ReturnsOkObjectResult()
        {
            // Arrange
            var rituals = new List<RitualDto>
            {
                new RitualDto { Id = 1, Title = "Ritual 1", Description = "Description 1" },
                new RitualDto { Id = 2,  Title = "Ritual 2", Description = "Description 2" }
            };

            _ritualServiceMock.Setup(service => service.GetAllRitualsAsync())
                .ReturnsAsync(rituals);

            // Act
            var result = await _controller.GetAllRituals();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(rituals, okResult.Value);
        }

       
    }
}
