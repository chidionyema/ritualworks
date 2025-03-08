using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Haworks.Infrastructure.Data;

namespace haworks.Services
{
    public class TokenCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TokenCleanupService> _logger;
        
        public TokenCleanupService(IServiceProvider serviceProvider, ILogger<TokenCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();
                    
                    // Remove expired revoked tokens
                    var expiredTokens = await context.RevokedTokens
                        .Where(t => t.ExpiryDate < DateTime.UtcNow)
                        .ToListAsync(stoppingToken);
                    
                    if (expiredTokens.Any())
                    {
                        _logger.LogInformation("Removing {Count} expired revoked tokens", expiredTokens.Count);
                        context.RevokedTokens.RemoveRange(expiredTokens);
                        await context.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during token cleanup");
                }
                
                // Run once per day
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
        }
    }
}