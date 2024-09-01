using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RitualWorks.Contracts;
using RitualWorks.Controllers;
using RitualWorks.Db;
using RitualWorks.Repositories;

namespace RitualWorks.Services
{
    public class RitualService : IRitualService
    {
        private readonly IRitualRepository _ritualRepository;

        public RitualService(IRitualRepository ritualRepository)
        {
            _ritualRepository = ritualRepository;
        }

        public async Task<RitualDto> CreateRitualAsync(CreateRitualDto ritualDto)
        {
            var ritual = MapToRitual(ritualDto);
            var createdRitual = await _ritualRepository.CreateRitualAsync(ritual);
            return MapToRitualDto(createdRitual);
        }

        public async Task<RitualDto?> UpdateRitualAsync(int id, CreateRitualDto ritualDto)
        {
            var ritual = await _ritualRepository.GetRitualByIdAsync(id);
            if (ritual == null || ritual.IsLocked)
            {
                return null;
            }

            UpdateRitualFromDto(ritual, ritualDto);
            var updatedRitual = await _ritualRepository.UpdateRitualAsync(ritual);
            return MapToRitualDto(updatedRitual);
        }

        public async Task<RitualDto?> GetRitualByIdAsync(int id)
        {
            var ritual = await _ritualRepository.GetRitualByIdAsync(id);
            return ritual == null ? null : MapToRitualDto(ritual);
        }

        public async Task<IEnumerable<RitualDto>> GetAllRitualsAsync()
        {
            var rituals = await _ritualRepository.GetAllRitualsAsync();
            return rituals.Select(MapToRitualDto);
        }

        public async Task<bool> LockRitualAsync(int id)
        {
            return await _ritualRepository.LockRitualAsync(id);
        }

        public async Task<bool> RateRitualAsync(int id, double rating)
        {
            return await _ritualRepository.RateRitualAsync(id, rating);
        }

        private static Ritual MapToRitual(CreateRitualDto ritualDto)
        {
            return new Ritual
            {
                Title = ritualDto.Title,
                Description = ritualDto.Description,
                Preview = ritualDto.Preview,
                FullTextContent = ritualDto.FullContent,
                TokenAmount = ritualDto.TokenAmount,
                RitualType = ritualDto.RitualType,
                MediaUrl = ritualDto.MediaUrl // Directly using the media URL provided by the frontend
            };
        }

        private static void UpdateRitualFromDto(Ritual ritual, CreateRitualDto ritualDto)
        {
            ritual.Title = ritualDto.Title;
            ritual.Description = ritualDto.Description;
            ritual.Preview = ritualDto.Preview;
            ritual.FullTextContent = ritualDto.FullContent;
            ritual.TokenAmount = ritualDto.TokenAmount;
            ritual.RitualType = ritualDto.RitualType;
            ritual.MediaUrl = ritualDto.MediaUrl; // Directly using the media URL provided by the frontend
        }

        private static RitualDto MapToRitualDto(Ritual ritual)
        {
            return new RitualDto
            {
                Id = ritual.Id,
                Title = ritual.Title,
                Description = ritual.Description,
                Preview = ritual.Preview,
                FullTextContent = ritual.FullTextContent,
                TokenAmount = ritual.TokenAmount,
                RitualType = ritual.RitualType,
                MediaUrl = ritual.MediaUrl,
                IsLocked = ritual.IsLocked,
                Rating = ritual.Rating
            };
        }
    }
}
