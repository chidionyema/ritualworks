using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using haworks.Db;

namespace haworks.Repositories.Base
{
    public abstract class BaseRepository<TEntity, TContext>
        where TEntity : AuditableEntity
        where TContext : DbContext
    {
        protected readonly TContext Context;
        protected readonly ILogger Logger;
        protected readonly IMemoryCache MemoryCache;
        // Commented out distributed cache for now
        // protected readonly IDistributedCache DistributedCache;

        // Cache settings
        protected readonly TimeSpan MemoryCacheDuration = TimeSpan.FromMinutes(10);
        // protected readonly TimeSpan DistributedCacheDuration = TimeSpan.FromHours(24);

        private static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve, // Handle circular references
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        protected BaseRepository(
            TContext context,
            ILogger logger,
            IMemoryCache memoryCache,
            /*IDistributedCache distributedCache*/ object distributedCachePlaceholder = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            MemoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            // DistributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
        }

        protected async Task<T?> GetFromCacheAsync<T>(string cacheKey, Func<Task<T?>> getDatabaseValue)
        {
            try
            {
                // Try memory cache first
                if (MemoryCache.TryGetValue(cacheKey, out T? memoryValue))
                {
                    Logger.LogInformation("Value retrieved from memory cache for key {CacheKey}.", cacheKey);
                    return memoryValue;
                }

                /*
                // Distributed cache code is commented out for now:
                string? cachedData = await DistributedCache.GetStringAsync(cacheKey);
                if (!string.IsNullOrEmpty(cachedData))
                {
                    var value = JsonSerializer.Deserialize<T>(cachedData, JsonSerializerOptions);
                    MemoryCache.Set(cacheKey, value, MemoryCacheDuration);
                    Logger.LogInformation("Value retrieved from distributed cache for key {CacheKey}.", cacheKey);
                    return value;
                }
                */

                // Get value from database
                var dbValue = await getDatabaseValue();
                if (dbValue != null)
                {
                    // Optionally cache in memory
                    MemoryCache.Set(cacheKey, dbValue, MemoryCacheDuration);
                    /*
                    // Optionally, if you later want to cache in distributed cache, uncomment:
                    await SetCacheValuesAsync(cacheKey, dbValue);
                    */
                }

                return dbValue;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error occurred while retrieving value for key {CacheKey}.", cacheKey);
                throw;
            }
        }

        protected async Task SetCacheValuesAsync<T>(string key, T value)
        {
            try
            {
                /*
                // Distributed cache code commented out:
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = DistributedCacheDuration
                };

                string serializedData = JsonSerializer.Serialize(value, JsonSerializerOptions);
                await DistributedCache.SetStringAsync(key, serializedData, options);
                */
                // Use memory cache only:
                MemoryCache.Set(key, value, MemoryCacheDuration);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error occurred while setting cache values for key {CacheKey}.", key);
                throw;
            }
        }

        protected async Task RemoveFromCacheAsync(string key)
        {
            try
            {
                /*
                // Commented out distributed cache removal:
                await DistributedCache.RemoveAsync(key);
                */
                MemoryCache.Remove(key);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error occurred while removing cache value for key {CacheKey}.", key);
                throw;
            }
        }

        protected async Task RemoveFromCacheAsync(IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                await RemoveFromCacheAsync(key);
            }
        }

        public async Task SaveChangesAsync()
        {
            try
            {
                Logger.LogInformation("Saving changes to the database.");
                await Context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error occurred while saving changes to the database.");
                throw new ApplicationException("An error occurred while saving changes to the database.", ex);
            }
        }

        protected int NormalizePage(int page) => page < 1 ? 1 : page;
        protected int NormalizePageSize(int pageSize) => pageSize < 1 ? 10 : pageSize;
    }
}
