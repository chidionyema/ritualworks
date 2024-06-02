using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RitualWorks.Contracts;
using RitualWorks.DTOs;
using RitualWorks.Db;

namespace RitualWorks.Services
{
    public class PetitionService : IPetitionService
    {
        private readonly IPetitionRepository _petitionRepository;
        private readonly IRitualRepository _ritualRepository;
        private readonly IUserRepository _userRepository;

        public PetitionService(IPetitionRepository petitionRepository, IRitualRepository ritualRepository, IUserRepository userRepository)
        {
            _petitionRepository = petitionRepository;
            _ritualRepository = ritualRepository;
            _userRepository = userRepository;
        }

        public async Task<PetitionDto> CreatePetitionAsync(CreatePetitionDto createPetitionDto)
        {
            var petition = new Petition
            {
                RitualId = createPetitionDto.RitualId,
                Description = createPetitionDto.Description,
                UserId = createPetitionDto.UserId
            };

            var createdPetition = await _petitionRepository.CreatePetitionAsync(petition);

            return new PetitionDto
            {
                Id = createdPetition.Id,
                RitualId = createdPetition.RitualId,
                UserId = createdPetition.UserId,
                Description = createdPetition.Description,
                Created = createdPetition.Created
            };
        }

        public async Task<PetitionDto?> GetPetitionByIdAsync(int id)
        {
            var petition = await _petitionRepository.GetPetitionByIdAsync(id);
            if (petition == null)
            {
                return null;
            }

            return new PetitionDto
            {
                Id = petition.Id,
                RitualId = petition.RitualId,
                UserId = petition.UserId,
                Description = petition.Description,
                Created = petition.Created
            };
        }

        public async Task<IEnumerable<PetitionDto>> GetPetitionsByRitualIdAsync(int ritualId)
        {
            var petitions = await _petitionRepository.GetPetitionsByRitualIdAsync(ritualId);
            return petitions.Select(p => new PetitionDto
            {
                Id = p.Id,
                RitualId = p.RitualId,
                UserId = p.UserId,
                Description = p.Description,
                Created = p.Created
            });
        }
    }
}
