using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RitualWorks.Contracts;
using RitualWorks.Db;

namespace RitualWorks.Repositories
{
    public class RitualRepository : IRitualRepository
    {
        private readonly RitualWorksContext _context;

        public RitualRepository(RitualWorksContext context)
        {
            _context = context;
        }

        public async Task<Ritual> CreateRitualAsync(Ritual ritual)
        {
            _context.Rituals.Add(ritual);
            await _context.SaveChangesAsync();
            return ritual;
        }

        public async Task<Ritual?> GetRitualByIdAsync(int id)
        {
            return await _context.Rituals
                .Include(r => r.Creator)
                .Include(r => r.Petitions)
                .Include(r => r.Donations)
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<IEnumerable<Ritual>> GetAllRitualsAsync()
        {
            return await _context.Rituals
                .Include(r => r.Creator)
                .Include(r => r.Petitions)
                .Include(r => r.Donations)
                .ToListAsync();
        }

        public async Task<Ritual?> UpdateRitualAsync(Ritual ritual)
        {
            _context.Rituals.Update(ritual);
            await _context.SaveChangesAsync();
            return ritual;
        }

        public async Task<bool> LockRitualAsync(int id)
        {
            var ritual = await _context.Rituals.FindAsync(id);
            if (ritual == null)
            {
                return false;
            }

            ritual.IsLocked = true;
            _context.Rituals.Update(ritual);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RateRitualAsync(int id, double rating)
        {
            var ritual = await _context.Rituals.FindAsync(id);
            if (ritual == null)
            {
                return false;
            }

            // Update the average rating logic
            // Assuming a simple average for demonstration
            ritual.Rating = (ritual.Rating + rating) / 2;
            _context.Rituals.Update(ritual);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<Ritual>> SearchRitualsAsync(string query, RitualTypeEnum? type)
        {
            var queryable = _context.Rituals.AsQueryable();

            if (!string.IsNullOrEmpty(query))
            {
                queryable = queryable.Where(r => r.Title.Contains(query) || r.Description.Contains(query));
            }

            if (type.HasValue)
            {
                queryable = queryable.Where(r => r.RitualType == type);
            }

            return await queryable
                .Include(r => r.Creator)
                .Include(r => r.Petitions)
                .Include(r => r.Donations)
                .ToListAsync();
        }
    }
}
