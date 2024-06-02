using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Moq;
using RitualWorks.Contracts;
using RitualWorks.DTOs;
using RitualWorks.Db;
using RitualWorks.Services;
using RitualWorks.Settings;
using Xunit;
using Azure;
using Azure.Storage.Blobs.Models;

public class RitualServiceTests
{
    private readonly Mock<IRitualRepository> _ritualRepositoryMock;
    private readonly Mock<BlobServiceClient> _blobServiceClientMock;
    private readonly Mock<IOptions<BlobSettings>> _blobSettingsMock;
    private readonly RitualService _ritualService;
    private readonly Mock<BlobClient> _blobClientMock;

    public RitualServiceTests()
    {
        _ritualRepositoryMock = new Mock<IRitualRepository>();
        _blobServiceClientMock = new Mock<BlobServiceClient>();
        _blobSettingsMock = new Mock<IOptions<BlobSettings>>();
        _blobSettingsMock.Setup(settings => settings.Value).Returns(new BlobSettings { ContainerName = "test-container" });

        _blobClientMock = new Mock<BlobClient>();

        _ritualService = new RitualService(_ritualRepositoryMock.Object, _blobServiceClientMock.Object, _blobSettingsMock.Object);
    }

    [Fact]
    public async Task CreateRitualAsync_ShouldCreateRitualWithoutMedia()
    {
        // Arrange
        var createRitualDto = new CreateRitualDto
        {
            Title = "Test Ritual",
            Description = "Description",
            Preview = "Preview",
            FullContent = "Full Content",
            ExternalLink = "http://example.com",
            TokenAmount = 10.0m,
            RitualType = RitualTypeEnum.Ceremonial
        };

        var ritual = new Ritual
        {
            Id = 1,
            Title = "Test Ritual",
            Description = "Description",
            Preview = "Preview",
            FullContent = "Full Content",
            ExternalLink = "http://example.com",
            TokenAmount = 10.0m,
            RitualType = RitualTypeEnum.Ceremonial,
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
        Assert.Equal(ritual.FullContent, result.FullContent);
        Assert.Equal(ritual.ExternalLink, result.ExternalLink);
        Assert.Equal(ritual.TokenAmount, result.TokenAmount);
        Assert.Equal(ritual.RitualType, result.RitualType);
        Assert.Equal(ritual.IsLocked, result.IsLocked);
        Assert.Equal(ritual.Rating, result.Rating);
        _ritualRepositoryMock.Verify(repo => repo.CreateRitualAsync(It.IsAny<Ritual>()), Times.Once);
    }

    [Fact]
    public async Task CreateRitualAsync_ShouldUploadMediaAndCreateRitual()
    {
        // Arrange
        var createRitualDto = new CreateRitualDto
        {
            Title = "Test Ritual",
            Description = "Description",
            Preview = "Preview",
            FullContent = "Full Content",
            ExternalLink = "http://example.com",
            TokenAmount = 10.0m,
            RitualType = RitualTypeEnum.Ceremonial
        };

        var ritual = new Ritual
        {
            Id = 1,
            Title = "Test Ritual",
            Description = "Description",
            Preview = "Preview",
            FullContent = "Full Content",
            ExternalLink = "http://example.com",
            TokenAmount = 10.0m,
            RitualType = RitualTypeEnum.Ceremonial,
            IsLocked = false,
            Rating = 0.0
        };

        var mediaStream = new MemoryStream();
        var blobContainerClientMock = new Mock<BlobContainerClient>();

        _blobServiceClientMock.Setup(client => client.GetBlobContainerClient(It.IsAny<string>())).Returns(blobContainerClientMock.Object);
        blobContainerClientMock.Setup(container => container.GetBlobClient(It.IsAny<string>())).Returns(_blobClientMock.Object);
        _blobClientMock.Setup(client => client.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default)).ReturnsAsync(new Mock<Response<BlobContentInfo>>().Object);

        _ritualRepositoryMock.Setup(repo => repo.CreateRitualAsync(It.IsAny<Ritual>())).ReturnsAsync(ritual);

        // Act
        var result = await _ritualService.CreateRitualAsync(createRitualDto, mediaStream);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ritual.Id, result.Id);
        Assert.Equal(ritual.Title, result.Title);
        Assert.Equal(ritual.Description, result.Description);
        Assert.Equal(ritual.Preview, result.Preview);
        Assert.Equal(ritual.FullContent, result.FullContent);
        Assert.Equal(ritual.ExternalLink, result.ExternalLink);
        Assert.Equal(ritual.TokenAmount, result.TokenAmount);
        Assert.Equal(ritual.RitualType, result.RitualType);
        Assert.Equal(ritual.IsLocked, result.IsLocked);
        Assert.Equal(ritual.Rating, result.Rating);
        _ritualRepositoryMock.Verify(repo => repo.CreateRitualAsync(It.IsAny<Ritual>()), Times.Once);
        _blobServiceClientMock.Verify(client => client.GetBlobContainerClient(It.IsAny<string>()), Times.Once);
        blobContainerClientMock.Verify(container => container.GetBlobClient(It.IsAny<string>()), Times.Once);
        _blobClientMock.Verify(client => client.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default), Times.Once);
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
            ExternalLink = "http://updated.com",
            TokenAmount = 20.0m,
            RitualType = RitualTypeEnum.Meditation
        };

        _ritualRepositoryMock.Setup(repo => repo.GetRitualByIdAsync(ritualId)).ReturnsAsync((Ritual)null);

        // Act
        var result = await _ritualService.UpdateRitualAsync(ritualId, createRitualDto);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateRitualAsync_ShouldUpdateRitualWithoutMedia()
    {
        // Arrange
        var ritualId = 1;
        var createRitualDto = new CreateRitualDto
        {
            Title = "Updated Ritual",
            Description = "Updated Description",
            Preview = "Updated Preview",
            FullContent = "Updated Full Content",
            ExternalLink = "http://updated.com",
            TokenAmount = 20.0m,
            RitualType = RitualTypeEnum.Meditation
        };

        var ritual = new Ritual
        {
            Id = ritualId,
            Title = "Old Ritual",
            Description = "Old Description",
            Preview = "Old Preview",
            FullContent = "Old Full Content",
            ExternalLink = "http://old.com",
            TokenAmount = 10.0m,
            RitualType = RitualTypeEnum.Ceremonial,
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
        Assert.Equal(createRitualDto.FullContent, result.FullContent);
        Assert.Equal(createRitualDto.ExternalLink, result.ExternalLink);
        Assert.Equal(createRitualDto.TokenAmount, result.TokenAmount);
        Assert.Equal(createRitualDto.RitualType, result.RitualType);
        _ritualRepositoryMock.Verify(repo => repo.GetRitualByIdAsync(ritualId), Times.Once);
        _ritualRepositoryMock.Verify(repo => repo.UpdateRitualAsync(It.IsAny<Ritual>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRitualAsync_ShouldUploadMediaAndUpdateRitual()
    {
        // Arrange
        var ritualId = 1;
        var createRitualDto = new CreateRitualDto
        {
            Title = "Updated Ritual",
            Description = "Updated Description",
            Preview = "Updated Preview",
            FullContent = "Updated Full Content",
            ExternalLink = "http://updated.com",
            TokenAmount = 20.0m,
            RitualType = RitualTypeEnum.Meditation
        };

        var ritual = new Ritual
        {
            Id = ritualId,
            Title = "Old Ritual",
            Description = "Old Description",
            Preview = "Old Preview",
            FullContent = "Old Full Content",
            ExternalLink = "http://old.com",
            TokenAmount = 10.0m,
            RitualType = RitualTypeEnum.Ceremonial,
            IsLocked = false,
            Rating = 0.0
        };

        var mediaStream = new MemoryStream();
        var blobContainerClientMock = new Mock<BlobContainerClient>();
        var blobClientMock = new Mock<BlobClient>();

        _blobServiceClientMock.Setup(client => client.GetBlobContainerClient(It.IsAny<string>())).Returns(blobContainerClientMock.Object);
        blobContainerClientMock.Setup(container => container.GetBlobClient(It.IsAny<string>())).Returns(blobClientMock.Object);
        var responseMock = new Mock<Response<BlobContentInfo>>();
        blobClientMock.Setup(client => client.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default)).ReturnsAsync(responseMock.Object);

        _ritualRepositoryMock.Setup(repo => repo.GetRitualByIdAsync(ritualId)).ReturnsAsync(ritual);
        _ritualRepositoryMock.Setup(repo => repo.UpdateRitualAsync(It.IsAny<Ritual>())).ReturnsAsync(ritual);

        // Act
        var result = await _ritualService.UpdateRitualAsync(ritualId, createRitualDto, mediaStream);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ritual.Id, result.Id);
        Assert.Equal(createRitualDto.Title, result.Title);
        Assert.Equal(createRitualDto.Description, result.Description);
        Assert.Equal(createRitualDto.Preview, result.Preview);
        Assert.Equal(createRitualDto.FullContent, result.FullContent);
        Assert.Equal(createRitualDto.ExternalLink, result.ExternalLink);
        Assert.Equal(createRitualDto.TokenAmount, result.TokenAmount);
        Assert.Equal(createRitualDto.RitualType, result.RitualType);
        _ritualRepositoryMock.Verify(repo => repo.GetRitualByIdAsync(ritualId), Times.Once);
        _ritualRepositoryMock.Verify(repo => repo.UpdateRitualAsync(It.IsAny<Ritual>()), Times.Once);
        _blobServiceClientMock.Verify(client => client.GetBlobContainerClient(It.IsAny<string>()), Times.Once);
        blobContainerClientMock.Verify(container => container.GetBlobClient(It.IsAny<string>()), Times.Once);
        blobClientMock.Verify(client => client.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), default), Times.Once);
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
            FullContent = "Full Content",
            ExternalLink = "http://example.com",
            TokenAmount = 10.0m,
            RitualType = RitualTypeEnum.Ceremonial,
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
        Assert.Equal(ritual.FullContent, result.FullContent);
        Assert.Equal(ritual.ExternalLink, result.ExternalLink);
        Assert.Equal(ritual.TokenAmount, result.TokenAmount);
        Assert.Equal(ritual.RitualType, result.RitualType);
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
            new Ritual { Id = 1, Title = "Ritual1", Description = "Description1", Preview = "Preview1", FullContent = "FullContent1", ExternalLink = "http://link1.com", TokenAmount = 10.0m, RitualType = RitualTypeEnum.Ceremonial, IsLocked = false, Rating = 0.0 },
            new Ritual { Id = 2, Title = "Ritual2", Description = "Description2", Preview = "Preview2", FullContent = "FullContent2", ExternalLink = "http://link2.com", TokenAmount = 20.0m, RitualType = RitualTypeEnum.Meditation, IsLocked = false, Rating = 0.0 }
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

    [Fact]
    public async Task SearchRitualsAsync_ShouldReturnRituals()
    {
        // Arrange
        var query = "Test";
        var type = RitualTypeEnum.Ceremonial;
        var rituals = new List<Ritual>
        {
            new Ritual { Id = 1, Title = "Test Ritual1", Description = "Description1", Preview = "Preview1", FullContent = "FullContent1", ExternalLink = "http://link1.com", TokenAmount = 10.0m, RitualType = type, IsLocked = false, Rating = 0.0 },
            new Ritual { Id = 2, Title = "Test Ritual2", Description = "Description2", Preview = "Preview2", FullContent = "FullContent2", ExternalLink = "http://link2.com", TokenAmount = 20.0m, RitualType = type, IsLocked = false, Rating = 0.0 }
        };

        _ritualRepositoryMock.Setup(repo => repo.SearchRitualsAsync(query, type)).ReturnsAsync(rituals);

        // Act
        var result = await _ritualService.SearchRitualsAsync(query, type);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
        Assert.Equal(rituals[0].Id, result.ElementAt(0).Id);
        Assert.Equal(rituals[1].Id, result.ElementAt(1).Id);
        _ritualRepositoryMock.Verify(repo => repo.SearchRitualsAsync(query, type), Times.Once);
    }
}
