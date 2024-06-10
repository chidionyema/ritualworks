using System;
using RitualWorks.Contracts;
using RitualWorks.Db;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
namespace RitualWorks.Repositories
{
    public class PostRepository : IPostRepository
    {
        private readonly RitualWorksContext _context;

        public PostRepository(RitualWorksContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Post>> GetPostsAsync()
        {
            return await _context.Posts.ToListAsync();
        }

        public async Task<Post> GetPostByIdAsync(Guid id)
        {
            return await _context.Posts.FindAsync(id);
        }

        public async Task AddPostAsync(Post post)
        {
            await _context.Posts.AddAsync(post);
            await _context.SaveChangesAsync();
        }

        public async Task UpdatePostAsync(Post post)
        {
            _context.Posts.Update(post);
            await _context.SaveChangesAsync();
        }

        public async Task DeletePostAsync(Guid id)
        {
            var post = await _context.Posts.FindAsync(id);
            if (post != null)
            {
                _context.Posts.Remove(post);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<Post>> GetPostsWithCommentsAsync()
        {
            return await _context.Posts.Include(p => p.Comments).ToListAsync();
        }
    }
}

