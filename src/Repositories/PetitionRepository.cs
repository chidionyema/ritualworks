using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RitualWorks.Contracts;
using RitualWorks.Db;

namespace RitualWorks.Repositories
{
    public class PetitionRepository : IPetitionRepository
    {
        private readonly RitualWorksContext _context;

        public PetitionRepository(RitualWorksContext context)
        {
            _context = context;
        }

        public async Task<Petition> CreatePetitionAsync(Petition petition)
        {
            _context.Petitions.Add(petition);
            await _context.SaveChangesAsync();
            return petition;
        }

        public async Task<Petition?> GetPetitionByIdAsync(int id)
        {
            return await _context.Petitions
                .Include(p => p.Ritual)
                .Include(p => p.User)
                .Include(p => p.Donations)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<IEnumerable<Petition>> GetPetitionsByRitualIdAsync(int ritualId)
        {
            return await _context.Petitions
                .Where(p => p.RitualId == ritualId)
                .Include(p => p.User)
                .Include(p => p.Donations)
                .ToListAsync();
        }
    }
}
