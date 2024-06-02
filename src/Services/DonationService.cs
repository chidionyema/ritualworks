using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RitualWorks.Contracts;
using RitualWorks.DTOs;
using RitualWorks.Db;

namespace RitualWorks.Services
{
    public class DonationService : IDonationService
    {
        private readonly IDonationRepository _donationRepository;
        private readonly IRitualService _ritualService;

        public DonationService(IDonationRepository donationRepository, IRitualService ritualService)
        {
            _donationRepository = donationRepository;
            _ritualService = ritualService;
        }

        public async Task<DonationDto> CreateDonationAsync(CreateDonationDto createDonationDto)
        {
            if (createDonationDto == null)
            {
                throw new ArgumentNullException(nameof(createDonationDto));
            }

            var donation = new Donation
            {
                Amount = createDonationDto.Amount,
                PetitionId = createDonationDto.PetitionId,
                RitualId = createDonationDto.RitualId,
                UserId = createDonationDto.UserId,
                Created = DateTime.UtcNow
            };

            var createdDonation = await _donationRepository.CreateDonationAsync(donation);

            if (createDonationDto.RitualId.HasValue)
            {
                var ritual = await _ritualService.GetRitualByIdAsync(createDonationDto.RitualId.Value);
                if (ritual != null && createDonationDto.Amount >= ritual.TokenAmount)
                {
                    await _ritualService.LockRitualAsync(createDonationDto.RitualId.Value);
                }
            }

            return new DonationDto
            {
                Id = createdDonation.Id,
                Amount = createdDonation.Amount,
                PetitionId = createdDonation.PetitionId,
                RitualId = createdDonation.RitualId,
                UserId = createdDonation.UserId
            };
        }

        public async Task<IEnumerable<DonationDto>> GetDonationsByPetitionIdAsync(int petitionId)
        {
            var donations = await _donationRepository.GetDonationsByPetitionIdAsync(petitionId);
            return donations.Select(d => new DonationDto
            {
                Id = d.Id,
                Amount = d.Amount,
                PetitionId = d.PetitionId,
                RitualId = d.RitualId,
                UserId = d.UserId
            });
        }

        public async Task<IEnumerable<DonationDto>> GetDonationsByRitualIdAsync(int ritualId)
        {
            var donations = await _donationRepository.GetDonationsByRitualIdAsync(ritualId);
            return donations.Select(d => new DonationDto
            {
                Id = d.Id,
                Amount = d.Amount,
                PetitionId = d.PetitionId,
                RitualId = d.RitualId,
                UserId = d.UserId
            });
        }

        public async Task<DonationDto?> GetDonationByIdAsync(int id)
        {
            var donation = await _donationRepository.GetDonationByIdAsync(id);
            if (donation == null)
            {
                return null;
            }

            return new DonationDto
            {
                Id = donation.Id,
                Amount = donation.Amount,
                PetitionId = donation.PetitionId,
                RitualId = donation.RitualId,
                UserId = donation.UserId
            };
        }
    }
}
