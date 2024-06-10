
using Moq;
using RitualWorks.Contracts;
using RitualWorks.Controllers;
using RitualWorks.Db;
using RitualWorks.Services;
using Xunit;

public class PetitionServiceTests
{
    private readonly Mock<IPetitionRepository> _petitionRepositoryMock;
    private readonly Mock<IRitualRepository> _ritualRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly PetitionService _petitionService;

    public PetitionServiceTests()
    {
        _petitionRepositoryMock = new Mock<IPetitionRepository>();
        _ritualRepositoryMock = new Mock<IRitualRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _petitionService = new PetitionService(_petitionRepositoryMock.Object, _ritualRepositoryMock.Object, _userRepositoryMock.Object);
    }

    [Fact]
    public async Task CreatePetitionAsync_ShouldThrowArgumentNullException_WhenCreatePetitionDtoIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _petitionService.CreatePetitionAsync(null));
    }

    [Fact]
    public async Task CreatePetitionAsync_ShouldCreatePetition()
    {
        // Arrange
        var createPetitionDto = new CreatePetitionDto
        {
            RitualId = 1,
            Description = "Petition Description",
            UserId = "user1"
        };
        var petition = new Petition
        {
            Id = 1,
            RitualId = 1,
            Description = "Petition Description",
            UserId = "user1",
            Created = DateTime.UtcNow
        };

        _petitionRepositoryMock.Setup(repo => repo.CreatePetitionAsync(It.IsAny<Petition>())).ReturnsAsync(petition);

        // Act
        var result = await _petitionService.CreatePetitionAsync(createPetitionDto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(petition.Id, result.Id);
        Assert.Equal(petition.RitualId, result.RitualId);
        Assert.Equal(petition.Description, result.Description);
        Assert.Equal(petition.UserId, result.UserId);
        Assert.Equal(petition.Created, result.Created);
        _petitionRepositoryMock.Verify(repo => repo.CreatePetitionAsync(It.IsAny<Petition>()), Times.Once);
    }

    [Fact]
    public async Task GetPetitionByIdAsync_ShouldReturnPetition_WhenPetitionExists()
    {
        // Arrange
        var petitionId = 1;
        var petition = new Petition
        {
            Id = petitionId,
            RitualId = 1,
            Description = "Petition Description",
            UserId = "user1",
            Created = DateTime.UtcNow
        };

        _petitionRepositoryMock.Setup(repo => repo.GetPetitionByIdAsync(petitionId)).ReturnsAsync(petition);

        // Act
        var result = await _petitionService.GetPetitionByIdAsync(petitionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(petitionId, result.Id);
        Assert.Equal(petition.RitualId, result.RitualId);
        Assert.Equal(petition.Description, result.Description);
        Assert.Equal(petition.UserId, result.UserId);
        Assert.Equal(petition.Created, result.Created);
    }

    [Fact]
    public async Task GetPetitionByIdAsync_ShouldReturnNull_WhenPetitionDoesNotExist()
    {
        // Arrange
        var petitionId = 1;

        _petitionRepositoryMock.Setup(repo => repo.GetPetitionByIdAsync(petitionId)).ReturnsAsync((Petition)null);

        // Act
        var result = await _petitionService.GetPetitionByIdAsync(petitionId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPetitionsByRitualIdAsync_ShouldReturnEmptyList_WhenNoPetitionsExist()
    {
        // Arrange
        var ritualId = 1;

        _petitionRepositoryMock.Setup(repo => repo.GetPetitionsByRitualIdAsync(ritualId)).ReturnsAsync(new List<Petition>());

        // Act
        var result = await _petitionService.GetPetitionsByRitualIdAsync(ritualId);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPetitionsByRitualIdAsync_ShouldReturnPetitions()
    {
        // Arrange
        var ritualId = 1;
        var petitions = new List<Petition>
        {
            new Petition { Id = 1, RitualId = ritualId, Description = "Description1", UserId = "user1", Created = DateTime.UtcNow },
            new Petition { Id = 2, RitualId = ritualId, Description = "Description2", UserId = "user2", Created = DateTime.UtcNow }
        };

        _petitionRepositoryMock.Setup(repo => repo.GetPetitionsByRitualIdAsync(ritualId)).ReturnsAsync(petitions);

        // Act
        var result = await _petitionService.GetPetitionsByRitualIdAsync(ritualId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
        Assert.Equal(petitions[0].Id, result.ElementAt(0).Id);
        Assert.Equal(petitions[1].Id, result.ElementAt(1).Id);
    }
}
