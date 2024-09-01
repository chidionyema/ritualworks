using System;
using System.Linq;
using RitualWorks.Contracts;
using RitualWorks.Db;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace RitualWorks.Repositories
{
    

    namespace RitualWorks.Repositories
    {
        public class CommentRepository : ICommentRepository
        {
            private readonly RitualWorksContext _context;

            public CommentRepository(RitualWorksContext context)
            {
                _context = context;
            }

            public async Task<IEnumerable<Comment>> GetCommentsAsync()
            {
                return await _context.Comments.ToListAsync();
            }

            public async Task<Comment> GetCommentByIdAsync(Guid id)
            {
                return await _context.Comments.FindAsync(id);
            }

            public async Task AddCommentAsync(Comment comment)
            {
                await _context.Comments.AddAsync(comment);
                await _context.SaveChangesAsync();
            }

            public async Task UpdateCommentAsync(Comment comment)
            {
                _context.Comments.Update(comment);
                await _context.SaveChangesAsync();
            }

            public async Task DeleteCommentAsync(Guid id)
            {
                var comment = await _context.Comments.FindAsync(id);
                if (comment != null)
                {
                    _context.Comments.Remove(comment);
                    await _context.SaveChangesAsync();
                }
            }

            public async Task<IEnumerable<Comment>> GetCommentsByPostIdAsync(Guid postId)
            {
                return await _context.Comments.Where(c => c.PostId == postId).ToListAsync();
            }
        }
    }

}

