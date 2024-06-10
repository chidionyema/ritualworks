using System;
using RitualWorks.Db;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RitualWorks.Contracts
{
    public interface ICommentRepository
    {
        Task<IEnumerable<Comment>> GetCommentsAsync();
        Task<Comment> GetCommentByIdAsync(Guid id);
        Task AddCommentAsync(Comment comment);
        Task UpdateCommentAsync(Comment comment);
        Task DeleteCommentAsync(Guid id);
        Task<IEnumerable<Comment>> GetCommentsByPostIdAsync(Guid postId);
    }
}

