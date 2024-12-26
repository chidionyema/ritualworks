using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using haworks.Contracts;
using haworks.Db;
using Microsoft.EntityFrameworkCore;

namespace haworks.Repositories
{
    public class ContentRepository : IContentRepository
    {
        private readonly haworksContext _context;

        public ContentRepository(haworksContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<IEnumerable<Content>> GetContentsByEntityIdAsync(Guid entityId, string entityType)
        {
            return await _context.Contents
                .Where(c => c.EntityId == entityId && c.EntityType == entityType)
                .ToListAsync();
        }

        public async Task AddContentsAsync(IEnumerable<Content> contents)
        {
            await _context.Contents.AddRangeAsync(contents);
        }

        public void RemoveContents(IEnumerable<Content> contents)
        {
            _context.Contents.RemoveRange(contents);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
