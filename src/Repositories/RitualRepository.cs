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
        private readonly IMemoryCache _memoryCache;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5); // Cache duration

        public RitualRepository(RitualWorksContext context, IMemoryCache memoryCache)
        {
            _context = context;
            _memoryCache = memoryCache;
        }

        public async Task<Ritual> CreateRitualAsync(Ritual ritual)
        {
            await _context.Rituals.AddAsync(ritual);
            await _context.SaveChangesAsync();
            InvalidateCache();
            return ritual;
        }

        public async Task<Ritual> UpdateRitualAsync(Ritual ritual)
        {
            _context.Rituals.Update(ritual);
            await _context.SaveChangesAsync();
            InvalidateCache();
            return ritual;
        }

        public async Task<Ritual> GetRitualByIdAsync(int id)
        {
            return await _context.Rituals.FindAsync(id);
        }

        public async Task<IEnumerable<Ritual>> GetAllRitualsAsync()
        {
            if (!_memoryCache.TryGetValue("AllRituals", out IEnumerable<Ritual> cachedRituals))
            {
                cachedRituals = await _context.Rituals.ToListAsync();
                var cacheEntryOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _cacheExpiration,
                    SlidingExpiration = TimeSpan.FromMinutes(2)
                };

                _memoryCache.Set("AllRituals", cachedRituals, cacheEntryOptions);
            }

            return cachedRituals;
        }

        public async Task<bool> LockRitualAsync(int id)
        {
            var ritual = await _context.Rituals.FindAsync(id);
            if (ritual == null) return false;

            ritual.IsLocked = true;
            await _context.SaveChangesAsync();
            InvalidateCache();
            return true;
        }

        public async Task<bool> RateRitualAsync(int id, double rating)
        {
            var ritual = await _context.Rituals.FindAsync(id);
            if (ritual == null) return false;

            ritual.Rating = (float)rating;
            await _context.SaveChangesAsync();
            InvalidateCache();
            return true;
        }

        public async Task<IEnumerable<Ritual>> SearchRitualsAsync(string query, RitualTypeEnum? type)
        {
            var cacheKey = $"Search-{query}-{type}";
            if (!_memoryCache.TryGetValue(cacheKey, out IEnumerable<Ritual> cachedRituals))
            {
                var ritualsQuery = _context.RitualFTS.FromSqlRaw("SELECT * FROM RitualsFTS WHERE RitualsFTS MATCH {0}", query).AsQueryable();

                if (type.HasValue)
                {
                    ritualsQuery = ritualsQuery.Where(r => r.RitualType == type);
                }

                cachedRituals = await ritualsQuery
                    .Join(_context.Rituals, fts => fts.Id, r => r.Id, (fts, r) => r)
                    .ToListAsync();

                var cacheEntryOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _cacheExpiration,
                    SlidingExpiration = TimeSpan.FromMinutes(2)
                };

                _memoryCache.Set(cacheKey, cachedRituals, cacheEntryOptions);
            }

            return cachedRituals;
        }

        private void InvalidateCache()
        {
            _memoryCache.Remove("AllRituals");
        }
    }
}
