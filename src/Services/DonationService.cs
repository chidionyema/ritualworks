using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RitualWorks.Contracts;
using RitualWorks.Db;
using RitualWorks.Controllers;
using RitualWorks.Repositories;
using Stripe.Checkout;

namespace RitualWorks.Services
{
    public class DonationService : IDonationService
    {
        private readonly IDonationRepository _donationRepository;
        private readonly IRitualService _ritualService;
        private readonly RitualWorksContext _context;

        public DonationService(IDonationRepository donationRepository, IRitualService ritualService, RitualWorksContext context)
        {
            _donationRepository = donationRepository;
            _ritualService = ritualService;
            _context = context;
        }

        public async Task<(DonationDto donation, string sessionId)> CreateDonationAsync(CreateDonationDto createDonationDto, string domain)
        {
            if (createDonationDto == null) throw new ArgumentNullException(nameof(createDonationDto));

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var donation = await CreateDonationRecordAsync(createDonationDto);
                await HandleRitualLockingAsync(createDonationDto);

                var sessionId = await CreatePaymentSessionAsync(createDonationDto.Amount, domain);

                await transaction.CommitAsync();

                return (MapToDonationDto(donation), sessionId);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<IEnumerable<DonationDto>> GetDonationsByPetitionIdAsync(int petitionId)
        {
            var donations = await _donationRepository.GetDonationsByPetitionIdAsync(petitionId);
            return donations.Select(MapToDonationDto);
        }

        public async Task<IEnumerable<DonationDto>> GetDonationsByRitualIdAsync(int ritualId)
        {
            var donations = await _donationRepository.GetDonationsByRitualIdAsync(ritualId);
            return donations.Select(MapToDonationDto);
        }

        public async Task<DonationDto?> GetDonationByIdAsync(int id)
        {
            var donation = await _donationRepository.GetDonationByIdAsync(id);
            return donation == null ? null : MapToDonationDto(donation);
        }

        private async Task<Donation> CreateDonationRecordAsync(CreateDonationDto createDonationDto)
        {
            var donation = new Donation
            {
                Amount = createDonationDto.Amount,
                PetitionId = createDonationDto.PetitionId,
                RitualId = createDonationDto.RitualId,
                UserId = createDonationDto.UserId,
                Created = DateTime.UtcNow
            };

            return await _donationRepository.CreateDonationAsync(donation);
        }

        private async Task HandleRitualLockingAsync(CreateDonationDto createDonationDto)
        {
            if (!createDonationDto.RitualId.HasValue) return;

            var ritual = await _ritualService.GetRitualByIdAsync(createDonationDto.RitualId.Value);
            if (ritual != null && createDonationDto.Amount >= ritual.TokenAmount)
            {
                await _ritualService.LockRitualAsync(createDonationDto.RitualId.Value);
            }
        }

        private async Task<string> CreatePaymentSessionAsync(decimal amount, string domain)
        {
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)(amount * 100),
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = "Donation",
                            },
                        },
                        Quantity = 1,
                    }
                },
                Mode = "payment",
                SuccessUrl = $"{domain}/donation/success?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{domain}/donation/cancel",
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return session.Id;
        }

        private DonationDto MapToDonationDto(Donation donation)
        {
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
