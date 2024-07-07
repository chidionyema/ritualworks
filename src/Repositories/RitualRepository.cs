using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
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
            await _context.Rituals.AddAsync(ritual);
            await _context.SaveChangesAsync();
            return ritual;
        }

        public async Task<Ritual> UpdateRitualAsync(Ritual ritual)
        {
            _context.Rituals.Update(ritual);
            await _context.SaveChangesAsync();
            return ritual;
        }

        public async Task<Ritual> GetRitualByIdAsync(int id)
        {
            return await _context.Rituals.FindAsync(id);
        }

        public async Task<IEnumerable<Ritual>> GetAllRitualsAsync()
        {
            return await _context.Rituals.ToListAsync(); ;
        }

        public async Task<bool> LockRitualAsync(int id)
        {
            var ritual = await _context.Rituals.FindAsync(id);
            if (ritual == null) return false;

            ritual.IsLocked = true;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RateRitualAsync(int id, double rating)
        {
            var ritual = await _context.Rituals.FindAsync(id);
            if (ritual == null) return false;

            ritual.Rating = (float)rating;
            await _context.SaveChangesAsync();
            return true;
        }

    }
}
