using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RitualWorks.Contracts;
using RitualWorks.Controllers;
using RitualWorks.Db;
using RitualWorks.Services;
using Xunit;

namespace RitualWorks.Tests.Unit.Services
{
    public class RitualTypeServiceTests
    {
        private readonly RitualTypeService _ritualTypeService;

        public RitualTypeServiceTests()
        {
            _ritualTypeService = new RitualTypeService();
        }

        [Fact]
        public async Task GetRitualTypesAsync_ShouldReturnAllRitualTypes()
        {
            // Arrange
            var expectedRitualTypes = Enum.GetValues(typeof(RitualTypeEnum)).Cast<RitualTypeEnum>().Select(e => new RitualTypeDto
            {
                Name = e.ToString(),
                Value = (int)e
            }).ToList();

            // Act
            var result = await _ritualTypeService.GetRitualTypesAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedRitualTypes.Count, result.Count());
            foreach (var expectedRitualType in expectedRitualTypes)
            {
                var actualRitualType = result.SingleOrDefault(r => r.Value == expectedRitualType.Value);
                Assert.NotNull(actualRitualType);
                Assert.Equal(expectedRitualType.Name, actualRitualType.Name);
                Assert.Equal(expectedRitualType.Value, actualRitualType.Value);
            }
        }

        [Fact]
        public async Task GetRitualTypesAsync_ShouldReturnEmptyList_WhenNoRitualTypesExist()
        {
            // Temporarily override enum values
            var originalValues = Enum.GetValues(typeof(RitualTypeEnum)).Cast<RitualTypeEnum>().ToArray();
            Enum.GetValues(typeof(RitualTypeEnum)).Cast<RitualTypeEnum>().ToList().ForEach(e => Enum.GetValues(typeof(RitualTypeEnum)).Cast<RitualTypeEnum>().ToList().Remove(e));

            try
            {
                // Act
                var result = await _ritualTypeService.GetRitualTypesAsync();

                // Assert
                Assert.NotNull(result);
                Assert.Empty(result);
            }
            finally
            {
                // Restore the original enum values for other tests
                originalValues.ToList().ForEach(e => Enum.GetValues(typeof(RitualTypeEnum)).Cast<RitualTypeEnum>().ToList().Add(e));
            }
        }

        [Fact]
        public async Task GetRitualTypesAsync_ShouldHandleEnumWithNewValues()
        {
            // Arrange
            var extendedEnumValues = Enum.GetValues(typeof(RitualTypeEnum)).Cast<RitualTypeEnum>().ToList();
            extendedEnumValues.Add((RitualTypeEnum)999); // Simulate a new value

            // Act
            var result = await _ritualTypeService.GetRitualTypesAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(extendedEnumValues.Count, result.Count());
            foreach (var enumValue in extendedEnumValues)
            {
                var actualRitualType = result.SingleOrDefault(r => r.Value == (int)enumValue);
                Assert.NotNull(actualRitualType);
                Assert.Equal(enumValue.ToString(), actualRitualType.Name);
                Assert.Equal((int)enumValue, actualRitualType.Value);
            }
        }
    }
}
