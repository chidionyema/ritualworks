// CredentialRefreshService.cs

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RitualWorks.Contracts;

namespace RitualWorks.Services // Adjust the namespace as needed
{
    public class CredentialRefreshService : BackgroundService
    {
        private readonly ILogger<CredentialRefreshService> _logger;
        private readonly IConnectionStringProvider _connectionStringProvider;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

        public CredentialRefreshService(
            ILogger<CredentialRefreshService> logger,
            IConnectionStringProvider connectionStringProvider)
        {
            _logger = logger;
            _connectionStringProvider = connectionStringProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Refreshing database credentials.");

                    _connectionStringProvider.UpdateConnectionString();

                    _logger.LogInformation("Database credentials refreshed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error refreshing database credentials.");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }
    }
}
