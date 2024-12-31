using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using haworks.Contracts;
using haworks.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace haworks.Repositories
{
    public class ContentRepository : IContentRepository
    {
        private readonly haworksContext _context;
        private readonly ILogger<ContentRepository> _logger;

        public ContentRepository(haworksContext context, ILogger<ContentRepository> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Content?> GetContentByIdAsync(Guid id)
        {
            try
            {
                _logger.LogInformation("Fetching content with ID {ContentId}", id);
                return await _context.Contents.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching content with ID {ContentId}", id);
                throw new ApplicationException($"An error occurred while retrieving the content with ID {id}.", ex);
            }
        }

        public async Task<IEnumerable<Content>> GetContentsByEntityIdAsync(Guid entityId, string entityType)
        {
            try
            {
                _logger.LogInformation("Fetching contents for entity ID {EntityId} and entity type {EntityType}", entityId, entityType);

                return await _context.Contents
                    .AsNoTracking()
                    .Where(c => c.EntityId == entityId && c.EntityType == entityType)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching contents for entity ID {EntityId} and entity type {EntityType}", entityId, entityType);
                throw new ApplicationException($"An error occurred while retrieving contents for entity ID {entityId} and entity type {entityType}.", ex);
            }
        }

        public async Task AddContentsAsync(IEnumerable<Content> contents)
        {
            if (contents == null || !contents.Any())
            {
                _logger.LogWarning("Attempted to add empty or null contents list.");
                throw new ArgumentException("Contents list cannot be null or empty.", nameof(contents));
            }

            try
            {
                _logger.LogInformation("Adding {ContentCount} contents to the database.", contents.Count());
                await _context.Contents.AddRangeAsync(contents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while adding contents to the database.");
                throw new ApplicationException("An error occurred while adding contents to the database.", ex);
            }
        }

        public void RemoveContents(IEnumerable<Content> contents)
        {
            if (contents == null || !contents.Any())
            {
                _logger.LogWarning("Attempted to remove empty or null contents list.");
                throw new ArgumentException("Contents list cannot be null or empty.", nameof(contents));
            }

            try
            {
                _logger.LogInformation("Removing {ContentCount} contents from the database.", contents.Count());
                _context.Contents.RemoveRange(contents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing contents from the database.");
                throw new ApplicationException("An error occurred while removing contents from the database.", ex);
            }
        }


        public void RemoveContent(Content content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            try
            {
                _logger.LogInformation("Removing content with EntityId {EntityId} from the database.", content.EntityId);

                // Mark the entity for removal
                _context.Remove(content);

                // Commit the changes to the database
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing content with EntityId {EntityId} from the database.", content?.EntityId);
                throw new ApplicationException("An error occurred while removing content from the database.", ex);
            }
        }


        public async Task SaveChangesAsync()
        {
            try
            {
                _logger.LogInformation("Saving changes to the database.");
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while saving changes to the database.");
                throw new ApplicationException("An error occurred while saving changes to the database.", ex);
            }
        }
    }
}
