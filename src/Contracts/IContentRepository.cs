using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using haworks.Db;

namespace haworks.Contracts
{
    public interface IContentRepository
    {
        Task<IEnumerable<Content>> GetContentsByEntityIdAsync(Guid entityId, string entityType);
        Task AddContentsAsync(IEnumerable<Content> contents);
        void RemoveContents(IEnumerable<Content> contents);
        Task SaveChangesAsync();
    }
}
