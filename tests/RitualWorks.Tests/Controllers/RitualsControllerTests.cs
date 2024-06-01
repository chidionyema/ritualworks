using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using RitualWorks.Db;
using RitualWorks.DTOs;
using RitualWorks.Services;
using System.Text;
using System.Text.Json;
using Xunit;
using System.Net.Http.Headers;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.TestHost;

public class RitualsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactory<Program> _factory;

    public RitualsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseInMemoryDatabase("TestDb");
                });

                services.AddScoped<IRitualService, RitualService>();
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task CreateRitual_ShouldReturnCreatedRitual()
    {
        // Arrange
        var ritualDto = new CreateRitualDto
        {
            Title = "Test Ritual",
            Description = "Test Description",
            TextContent = "Test Content",
            RitualTypeId = 1
        };

        using var content = new MultipartFormDataContent
        {
            { new StringContent(ritualDto.Title), "Title" },
            { new StringContent(ritualDto.Description), "Description" },
            { new StringContent(ritualDto.TextContent), "TextContent" },
            { new StringContent(ritualDto.RitualTypeId.ToString()), "RitualTypeId" },
            { new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("Test file content"))), "mediaFile", "test.txt" }
        };

        // Act
        var response = await _client.PostAsync("/api/rituals", content);
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        var createdRitual = JsonSerializer.Deserialize<RitualDto>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        Assert.NotNull(createdRitual);
        Assert.Equal(ritualDto.Title, createdRitual.Title);
        Assert.Equal(ritualDto.Description, createdRitual.Description);
        Assert.Equal(ritualDto.TextContent, createdRitual.TextContent);
        Assert.Equal(ritualDto.RitualTypeId, createdRitual.RitualTypeId);
        Assert.NotNull(createdRitual.MediaUrl);
    }

    [Fact]
    public async Task GetRitualById_ShouldReturnRitual()
    {
        // Arrange
        var createdRitual = await SeedDatabaseAsync();

        // Act
        var response = await _client.GetAsync($"/api/rituals/{createdRitual.Id}");
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        var ritual = JsonSerializer.Deserialize<RitualDto>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        Assert.NotNull(ritual);
        Assert.Equal(createdRitual.Title, ritual.Title);
        Assert.Equal(createdRitual.Description, ritual.Description);
        Assert.Equal(createdRitual.TextContent, ritual.TextContent);
        Assert.Equal(createdRitual.RitualTypeId, ritual.RitualTypeId);
        Assert.Equal(createdRitual.MediaUrl, ritual.MediaUrl);
    }

    [Fact]
    public async Task UpdateRitual_ShouldReturnUpdatedRitual()
    {
        // Arrange
        var createdRitual = await SeedDatabaseAsync();
        var updateDto = new CreateRitualDto
        {
            Title = "Updated Ritual",
            Description = "Updated Description",
            TextContent = "Updated Content",
            RitualTypeId = 2
        };

        using var content = new MultipartFormDataContent
        {
            { new StringContent(updateDto.Title), "Title" },
            { new StringContent(updateDto.Description), "Description" },
            { new StringContent(updateDto.TextContent), "TextContent" },
            { new StringContent(updateDto.RitualTypeId.ToString()), "RitualTypeId" },
            { new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("Updated file content"))), "mediaFile", "updated.txt" }
        };

        // Act
        var response = await _client.PutAsync($"/api/rituals/{createdRitual.Id}", content);
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        var updatedRitual = JsonSerializer.Deserialize<RitualDto>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        Assert.NotNull(updatedRitual);
        Assert.Equal(updateDto.Title, updatedRitual.Title);
        Assert.Equal(updateDto.Description, updatedRitual.Description);
        Assert.Equal(updateDto.TextContent, updatedRitual.TextContent);
        Assert.Equal(updateDto.RitualTypeId, updatedRitual.RitualTypeId);
        Assert.NotNull(updatedRitual.MediaUrl);
    }

    [Fact]
    public async Task GetAllRituals_ShouldReturnAllRituals()
    {
        // Arrange
        await SeedDatabaseAsync();

        // Act
        var response = await _client.GetAsync("/api/rituals");
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        var rituals = JsonSerializer.Deserialize<List<RitualDto>>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        Assert.NotNull(rituals);
        Assert.NotEmpty(rituals);
        Assert.All(rituals, ritual =>
        {
            Assert.NotNull(ritual.Title);
            Assert.NotNull(ritual.Description);
            Assert.NotNull(ritual.TextContent);
            Assert.NotNull(ritual.MediaUrl);
        });
    }

    [Fact]
    public async Task DeleteRitual_ShouldReturnNoContent()
    {
        // Arrange
        var createdRitual = await SeedDatabaseAsync();

        // Act
        var response = await _client.DeleteAsync($"/api/rituals/{createdRitual.Id}");
        response.EnsureSuccessStatusCode();

        var context = _factory.Services.GetRequiredService<ApplicationDbContext>();
        var deletedRitual = await context.Rituals.FindAsync(createdRitual.Id);

        // Assert
        Assert.Null(deletedRitual);
    }

    private async Task<Ritual> SeedDatabaseAsync()
    {
        var context = _factory.Services.GetRequiredService<ApplicationDbContext>();
        var ritual = new Ritual
        {
            Title = "Seeded Ritual",
            Description = "Seeded Description",
            TextContent = "Seeded Content",
            MediaUrl = "http://example.com/media",
            RitualTypeId = 1
        };

        context.Rituals.Add(ritual);
        await context.SaveChangesAsync();

        return ritual;
    }
}
