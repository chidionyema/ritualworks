using RitualWorks.DTOs;
using RitualWorks.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace RitualWorks.Tests.Services
{
    public class RitualTypeServiceTests
    {
        private readonly IRitualTypeService _ritualTypeService;

        public RitualTypeServiceTests()
        {
            _ritualTypeService = new RitualTypeService();
        }

        [Fact]
        public async Task GetRitualTypesAsync_ShouldReturnAllRitualTypes()
        {
            // Act
            var result = await _ritualTypeService.GetRitualTypesAsync();

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Contains(result, rt => rt.Name == "Ceremonial");
            Assert.Contains(result, rt => rt.Name == "Meditation");
        }
    }
}
