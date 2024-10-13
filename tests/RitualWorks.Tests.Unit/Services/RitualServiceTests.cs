using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using RitualWorks.Contracts;
using RitualWorks.Controllers;
using RitualWorks.Db;
using RitualWorks.Services;
using Xunit;

public class RitualServiceTests
{
    private readonly Mock<IRitualRepository> _ritualRepositoryMock;
    private readonly RitualService _ritualService;

    public RitualServiceTests()
    {
        _ritualRepositoryMock = new Mock<IRitualRepository>();
        _ritualService = new RitualService(_ritualRepositoryMock.Object);
    }

    [Fact]
    public async Task CreateRitualAsync_ShouldCreateRitual()
    {
        // Arrange
        var createRitualDto = new CreateRitualDto
        {
            Title = "Test Ritual",
            Description = "Description",
            Preview = "Preview",
            FullContent = "Full Content",
            TokenAmount = 10.0m,
            RitualType = RitualTypeEnum.Ceremonial,
            MediaUrl = "http://example.com/media"
        };

        var ritual = new Ritual
        {
            Id = 1,
            Title = "Test Ritual",
            Description = "Description",
            Preview = "Preview",
            FullTextContent = "Full Content",
            TokenAmount = 10.0m,
            RitualType = RitualTypeEnum.Ceremonial,
            MediaUrl = "http://example.com/media",
            IsLocked = false,
            Rating = 0.0
        };

        _ritualRepositoryMock.Setup(repo => repo.CreateRitualAsync(It.IsAny<Ritual>())).ReturnsAsync(ritual);

        // Act
        var result = await _ritualService.CreateRitualAsync(createRitualDto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ritual.Id, result.Id);
        Assert.Equal(ritual.Title, result.Title);
        Assert.Equal(ritual.Description, result.Description);
        Assert.Equal(ritual.Preview, result.Preview);
        Assert.Equal(ritual.FullTextContent, result.FullTextContent);
        Assert.Equal(ritual.TokenAmount, result.TokenAmount);
        Assert.Equal(ritual.RitualType, result.RitualType);
        Assert.Equal(ritual.MediaUrl, result.MediaUrl);
        _ritualRepositoryMock.Verify(repo => repo.CreateRitualAsync(It.IsAny<Ritual>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRitualAsync_ShouldReturnNull_WhenRitualDoesNotExist()
    {
        // Arrange
        var ritualId = 1;
        var createRitualDto = new CreateRitualDto
        {
            Title = "Updated Ritual",
            Description = "Updated Description",
            Preview = "Updated Preview",
            FullContent = "Updated Full Content",
            TokenAmount = 20.0m,
            RitualType = RitualTypeEnum.Meditation,
            MediaUrl = "http://updated.com/media"
        };

        _ritualRepositoryMock.Setup(repo => repo.GetRitualByIdAsync(ritualId)).ReturnsAsync((Ritual)null);

        // Act
        var result = await _ritualService.UpdateRitualAsync(ritualId, createRitualDto);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateRitualAsync_ShouldUpdateRitual()
    {
        // Arrange
        var ritualId = 1;
        var createRitualDto = new CreateRitualDto
        {
            Title = "Updated Ritual",
            Description = "Updated Description",
            Preview = "Updated Preview",
            FullContent = "Updated Full Content",
            TokenAmount = 20.0m,
            RitualType = RitualTypeEnum.Meditation,
            MediaUrl = "http://updated.com/media"
        };

        var ritual = new Ritual
        {
            Id = ritualId,
            Title = "Old Ritual",
            Description = "Old Description",
            Preview = "Old Preview",
            FullTextContent = "Old Full Content",
            TokenAmount = 10.0m,
            RitualType = RitualTypeEnum.Ceremonial,
            MediaUrl = "http://old.com/media",
            IsLocked = false,
            Rating = 0.0
        };

        _ritualRepositoryMock.Setup(repo => repo.GetRitualByIdAsync(ritualId)).ReturnsAsync(ritual);
        _ritualRepositoryMock.Setup(repo => repo.UpdateRitualAsync(It.IsAny<Ritual>())).ReturnsAsync(ritual);

        // Act
        var result = await _ritualService.UpdateRitualAsync(ritualId, createRitualDto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ritual.Id, result.Id);
        Assert.Equal(createRitualDto.Title, result.Title);
        Assert.Equal(createRitualDto.Description, result.Description);
        Assert.Equal(createRitualDto.Preview, result.Preview);
        Assert.Equal(createRitualDto.FullContent, result.FullTextContent);
        Assert.Equal(createRitualDto.TokenAmount, result.TokenAmount);
        Assert.Equal(createRitualDto.RitualType, result.RitualType);
        Assert.Equal(createRitualDto.MediaUrl, result.MediaUrl);
        _ritualRepositoryMock.Verify(repo => repo.GetRitualByIdAsync(ritualId), Times.Once);
        _ritualRepositoryMock.Verify(repo => repo.UpdateRitualAsync(It.IsAny<Ritual>()), Times.Once);
    }

    [Fact]
    public async Task GetRitualByIdAsync_ShouldReturnRitual_WhenRitualExists()
    {
        // Arrange
        var ritualId = 1;
        var ritual = new Ritual
        {
            Id = ritualId,
            Title = "Test Ritual",
            Description = "Description",
            Preview = "Preview",
            FullTextContent = "Full Content",
            TokenAmount = 10.0m,
            RitualType = RitualTypeEnum.Ceremonial,
            MediaUrl = "http://example.com/media",
            IsLocked = false,
            Rating = 0.0
        };

        _ritualRepositoryMock.Setup(repo => repo.GetRitualByIdAsync(ritualId)).ReturnsAsync(ritual);

        // Act
        var result = await _ritualService.GetRitualByIdAsync(ritualId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ritual.Id, result.Id);
        Assert.Equal(ritual.Title, result.Title);
        Assert.Equal(ritual.Description, result.Description);
        Assert.Equal(ritual.Preview, result.Preview);
        Assert.Equal(ritual.FullTextContent, result.FullTextContent);
        Assert.Equal(ritual.TokenAmount, result.TokenAmount);
        Assert.Equal(ritual.RitualType, result.RitualType);
        Assert.Equal(ritual.MediaUrl, result.MediaUrl);
        Assert.Equal(ritual.IsLocked, result.IsLocked);
        Assert.Equal(ritual.Rating, result.Rating);
        _ritualRepositoryMock.Verify(repo => repo.GetRitualByIdAsync(ritualId), Times.Once);
    }

    [Fact]
    public async Task GetRitualByIdAsync_ShouldReturnNull_WhenRitualDoesNotExist()
    {
        // Arrange
        var ritualId = 1;

        _ritualRepositoryMock.Setup(repo => repo.GetRitualByIdAsync(ritualId)).ReturnsAsync((Ritual)null);

        // Act
        var result = await _ritualService.GetRitualByIdAsync(ritualId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllRitualsAsync_ShouldReturnRituals()
    {
        // Arrange
        var rituals = new List<Ritual>
        {
            new Ritual { Id = 1, Title = "Ritual1", Description = "Description1", Preview = "Preview1", FullTextContent = "FullContent1", TokenAmount = 10.0m, RitualType = RitualTypeEnum.Ceremonial, MediaUrl = "http://link1.com/media", IsLocked = false, Rating = 0.0 },
            new Ritual { Id = 2, Title = "Ritual2", Description = "Description2", Preview = "Preview2", FullTextContent = "FullContent2", TokenAmount = 20.0m, RitualType = RitualTypeEnum.Meditation, MediaUrl = "http://link2.com/media", IsLocked = false, Rating = 0.0 }
        };

        _ritualRepositoryMock.Setup(repo => repo.GetAllRitualsAsync()).ReturnsAsync(rituals);

        // Act
        var result = await _ritualService.GetAllRitualsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
        Assert.Equal(rituals[0].Id, result.ElementAt(0).Id);
        Assert.Equal(rituals[1].Id, result.ElementAt(1).Id);
        _ritualRepositoryMock.Verify(repo => repo.GetAllRitualsAsync(), Times.Once);
    }

    [Fact]
    public async Task LockRitualAsync_ShouldLockRitual()
    {
        // Arrange
        var ritualId = 1;

        _ritualRepositoryMock.Setup(repo => repo.LockRitualAsync(ritualId)).ReturnsAsync(true);

        // Act
        var result = await _ritualService.LockRitualAsync(ritualId);

        // Assert
        Assert.True(result);
        _ritualRepositoryMock.Verify(repo => repo.LockRitualAsync(ritualId), Times.Once);
    }

    [Fact]
    public async Task RateRitualAsync_ShouldRateRitual()
    {
        // Arrange
        var ritualId = 1;
        var rating = 4.5;

        _ritualRepositoryMock.Setup(repo => repo.RateRitualAsync(ritualId, rating)).ReturnsAsync(true);

        // Act
        var result = await _ritualService.RateRitualAsync(ritualId, rating);

        // Assert
        Assert.True(result);
        _ritualRepositoryMock.Verify(repo => repo.RateRitualAsync(ritualId, rating), Times.Once);
    }

}
