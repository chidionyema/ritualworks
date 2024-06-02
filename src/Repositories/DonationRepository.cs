using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RitualWorks.Contracts;
using RitualWorks.Db;

namespace RitualWorks.Repositories
{
    public class DonationRepository : IDonationRepository
    {
        private readonly RitualWorksContext _context;

        public DonationRepository(RitualWorksContext context)
        {
            _context = context;
        }

        public async Task<Donation> CreateDonationAsync(Donation donation)
        {
            _context.Donations.Add(donation);
            await _context.SaveChangesAsync();
            return donation;
        }

        public async Task<Donation?> GetDonationByIdAsync(int id)
        {
            return await _context.Donations
                .Include(d => d.Petition)
                .Include(d => d.Ritual)
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<IEnumerable<Donation>> GetDonationsByPetitionIdAsync(int petitionId)
        {
            return await _context.Donations
                .Where(d => d.PetitionId == petitionId)
                .Include(d => d.User)
                .ToListAsync();
        }

        public async Task<IEnumerable<Donation>> GetDonationsByRitualIdAsync(int ritualId)
        {
            return await _context.Donations
                .Where(d => d.RitualId == ritualId)
                .Include(d => d.User)
                .ToListAsync();
        }
    }
}
