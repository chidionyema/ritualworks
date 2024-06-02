using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using RitualWorks.Contracts;
using RitualWorks.Controllers;
using RitualWorks.DTOs;
using Xunit;

namespace RitualWorks.Tests.Controllers
{
    public class PetitionsControllerTests
    {
        private readonly Mock<IPetitionService> _petitionServiceMock;
        private readonly PetitionsController _petitionsController;

        public PetitionsControllerTests()
        {
            _petitionServiceMock = new Mock<IPetitionService>();
            _petitionsController = new PetitionsController(_petitionServiceMock.Object);
        }

        [Fact]
        public async Task CreatePetition_ShouldReturnCreatedResult_WhenPetitionIsCreated()
        {
            // Arrange
            var createPetitionDto = new CreatePetitionDto
            {
                RitualId = 1,
                Description = "Test Petition",
                UserId = "user1"
            };

            var petitionDto = new PetitionDto
            {
                Id = 1,
                RitualId = createPetitionDto.RitualId,
                Description = createPetitionDto.Description,
                UserId = createPetitionDto.UserId,
                Created = System.DateTime.UtcNow
            };

            _petitionServiceMock.Setup(service => service.CreatePetitionAsync(createPetitionDto))
                .ReturnsAsync(petitionDto);

            // Act
            var result = await _petitionsController.CreatePetition(createPetitionDto);

            // Assert
            var createdAtActionResult = Assert.IsType<CreatedAtActionResult>(result.Result);
            Assert.Equal(nameof(_petitionsController.GetPetitionById), createdAtActionResult.ActionName);
            Assert.Equal(petitionDto.Id, createdAtActionResult.RouteValues["id"]);
            Assert.Equal(petitionDto, createdAtActionResult.Value);
        }

        [Fact]
        public async Task GetPetitionById_ShouldReturnOkResult_WhenPetitionExists()
        {
            // Arrange
            var petitionId = 1;
            var petitionDto = new PetitionDto
            {
                Id = petitionId,
                RitualId = 1,
                Description = "Test Petition",
                UserId = "user1",
                Created = System.DateTime.UtcNow
            };

            _petitionServiceMock.Setup(service => service.GetPetitionByIdAsync(petitionId))
                .ReturnsAsync(petitionDto);

            // Act
            var result = await _petitionsController.GetPetitionById(petitionId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(petitionDto, okResult.Value);
        }

        [Fact]
        public async Task GetPetitionById_ShouldReturnNotFoundResult_WhenPetitionDoesNotExist()
        {
            // Arrange
            var petitionId = 1;
            _petitionServiceMock.Setup(service => service.GetPetitionByIdAsync(petitionId))
                .ReturnsAsync((PetitionDto)null);

            // Act
            var result = await _petitionsController.GetPetitionById(petitionId);

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
        }

        [Fact]
        public async Task GetPetitionsByRitualId_ShouldReturnOkResult_WhenPetitionsExist()
        {
            // Arrange
            var ritualId = 1;
            var petitions = new List<PetitionDto>
            {
                new PetitionDto { Id = 1, RitualId = ritualId, Description = "Petition 1", UserId = "user1", Created = System.DateTime.UtcNow },
                new PetitionDto { Id = 2, RitualId = ritualId, Description = "Petition 2", UserId = "user2", Created = System.DateTime.UtcNow }
            };

            _petitionServiceMock.Setup(service => service.GetPetitionsByRitualIdAsync(ritualId))
                .ReturnsAsync(petitions);

            // Act
            var result = await _petitionsController.GetPetitionsByRitualId(ritualId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var returnValue = Assert.IsType<List<PetitionDto>>(okResult.Value);
            Assert.Equal(petitions.Count, returnValue.Count);
        }

        [Fact]
        public async Task GetPetitionsByRitualId_ShouldReturnOkResult_WhenNoPetitionsExist()
        {
            // Arrange
            var ritualId = 1;
            var petitions = new List<PetitionDto>();

            _petitionServiceMock.Setup(service => service.GetPetitionsByRitualIdAsync(ritualId))
                .ReturnsAsync(petitions);

            // Act
            var result = await _petitionsController.GetPetitionsByRitualId(ritualId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var returnValue = Assert.IsType<List<PetitionDto>>(okResult.Value);
            Assert.Empty(returnValue);
        }
    }
}
