using System;
using RitualWorks.Db;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RitualWorks.Contracts
{
    public interface IPostRepository
    {
        Task<IEnumerable<Post>> GetPostsAsync();
        Task<Post> GetPostByIdAsync(Guid id);
        Task AddPostAsync(Post post);
        Task UpdatePostAsync(Post post);
        Task DeletePostAsync(Guid  id);
        Task<IEnumerable<Post>> GetPostsWithCommentsAsync();
    }
}

