using Microsoft.EntityFrameworkCore;
using RitualWorks.Contracts;
using RitualWorks.Db;
using RitualWorks.DTOs;
using RitualWorks.Repositories;
using RitualWorks.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace RitualWorks.Tests.Services
{
    public class PetitionServiceTests
    {
        private readonly RitualWorksContext _dbContext;
        private readonly IPetitionRepository _petitionRepository;
        private readonly IRitualRepository _ritualRepository;
        private readonly IUserRepository _userRepository;
        private readonly PetitionService _petitionService;

        public PetitionServiceTests()
        {
            // Set up SQLite in-memory database
            var options = new DbContextOptionsBuilder<RitualWorksContext>()
                .UseSqlite("Filename=:memory:")
                .Options;
            _dbContext = new RitualWorksContext(options);

            _dbContext.Database.OpenConnection();
            _dbContext.Database.EnsureCreated();

            // Initialize the repositories
            _petitionRepository = new PetitionRepository(_dbContext);
            _ritualRepository = new RitualRepository(_dbContext);
            _userRepository = new UserRepository(_dbContext);

            // Initialize the service with real instances
            _petitionService = new PetitionService(_petitionRepository, _ritualRepository, _userRepository);
        }

        [Fact]
        public async Task CreatePetitionAsync_ShouldReturnPetitionDto()
        {
            // Arrange
            var createPetitionDto = new CreatePetitionDto
            {
                RitualId = 1,
                Description = "Test Petition",
                UserId = "user1"
            };

            // Act
            var result = await _petitionService.CreatePetitionAsync(createPetitionDto);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.RitualId);
            Assert.Equal("Test Petition", result.Description);
            Assert.Equal("user1", result.UserId);

            // Verify persistence in the database
            var persistedPetition = await _dbContext.Petitions.FindAsync(result.Id);
            Assert.NotNull(persistedPetition);
            Assert.Equal(1, persistedPetition.RitualId);
            Assert.Equal("Test Petition", persistedPetition.Description);
            Assert.Equal("user1", persistedPetition.UserId);
        }

        [Fact]
        public async Task GetPetitionByIdAsync_ShouldReturnPetitionDto()
        {
            // Arrange
            var createdPetition = await SeedDatabaseAsync();

            // Act
            var result = await _petitionService.GetPetitionByIdAsync(createdPetition.Id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(createdPetition.Id, result.Id);
            Assert.Equal(createdPetition.RitualId, result.RitualId);
            Assert.Equal(createdPetition.Description, result.Description);
            Assert.Equal(createdPetition.UserId, result.UserId);
            Assert.Equal(createdPetition.Created, result.Created);
        }

        [Fact]
        public async Task GetPetitionsByRitualIdAsync_ShouldReturnPetitions()
        {
            // Arrange
            await SeedDatabaseAsync();

            // Act
            var result = await _petitionService.GetPetitionsByRitualIdAsync(1);

            // Assert
            Assert.NotEmpty(result);
            var firstPetition = result.FirstOrDefault();
            Assert.NotNull(firstPetition);
            Assert.Equal(1, firstPetition.RitualId);
        }

        private async Task<Petition> SeedDatabaseAsync()
        {
            var petition = new Petition
            {
                RitualId = 1,
                Description = "Seeded Petition",
                UserId = "user1",
                Created = DateTime.UtcNow
            };

            _dbContext.Petitions.Add(petition);
            await _dbContext.SaveChangesAsync();

            return petition;
        }
    }
}
