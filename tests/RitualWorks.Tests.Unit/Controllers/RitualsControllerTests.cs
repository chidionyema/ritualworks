using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Threading.Tasks;
using RitualWorks.Services;
using RitualWorks.Controllers;
using Xunit;
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
        public async Task CreateRitual_WithValidDto_ReturnsCreatedAtAction()
        {
            // Arrange
            var createRitualDto = new CreateRitualDto
            {
                Title = "Sample Ritual",
                Description = "Sample Description"
            };

            var createdRitual = new RitualDto
            {
                Id = 1,
                Title = "Sample Ritual",
                Description = "Sample Description"
            };

            _ritualServiceMock.Setup(service => service.CreateRitualAsync(createRitualDto))
                .ReturnsAsync(createdRitual);

            // Act
            var result = await _controller.CreateRitual(createRitualDto);

            // Assert
            var createdAtActionResult = Assert.IsType<CreatedAtActionResult>(result.Result);
            Assert.Equal(nameof(_controller.GetRitualById), createdAtActionResult.ActionName);
            Assert.Equal(createdRitual.Id, createdAtActionResult.RouteValues["id"]);
            Assert.Equal(createdRitual, createdAtActionResult.Value);
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

            var updatedRitual = new RitualDto
            {
                Id = 1,
                Title = "Updated Ritual",
                Description = "Updated Description"
            };

            _ritualServiceMock.Setup(service => service.UpdateRitualAsync(1, updateRitualDto))
                .ReturnsAsync(updatedRitual);

            // Act
            var result = await _controller.UpdateRitual(1, updateRitualDto);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
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

            _ritualServiceMock.Setup(service => service.UpdateRitualAsync(1, updateRitualDto))
                .ReturnsAsync((RitualDto)null);

            // Act
            var result = await _controller.UpdateRitual(1, updateRitualDto);

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
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
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
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
            Assert.IsType<NotFoundResult>(result.Result);
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
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(rituals, okResult.Value);
        }
    }
}
