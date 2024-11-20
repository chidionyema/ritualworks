// CredentialRefreshService.cs

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using haworks.Contracts;

namespace haworks.Services
{
    public class CredentialRefreshService : BackgroundService
    {
        private readonly ILogger<CredentialRefreshService> _logger;
        private readonly IConnectionStringProvider _connectionStringProvider;

        public CredentialRefreshService(
            ILogger<CredentialRefreshService> logger,
            IConnectionStringProvider connectionStringProvider)
        {
            _logger = logger;
            _connectionStringProvider = connectionStringProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            TimeSpan checkInterval = TimeSpan.FromMinutes(1); // Default interval

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Refreshing database credentials.");

                    _connectionStringProvider.UpdateConnectionString();

                    _logger.LogInformation("Database credentials refreshed.");

                    // Get the lease duration and adjust the interval
                    int leaseDuration = _connectionStringProvider.GetLeaseDuration();
                    checkInterval = TimeSpan.FromSeconds(leaseDuration * 0.8); // Refresh at 80% of TTL

                    _logger.LogInformation("Next credential refresh in {Interval} seconds.", checkInterval.TotalSeconds);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error refreshing database credentials.");

                    // Retry after a short delay on error
                    checkInterval = TimeSpan.FromSeconds(30);
                }

                await Task.Delay(checkInterval, stoppingToken);
            }
        }
    }
}
