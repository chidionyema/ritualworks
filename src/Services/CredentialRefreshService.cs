// Services/CredentialRefreshService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RitualWorks.Models;

namespace RitualWorks.Services
{
    public class CredentialRefreshService : BackgroundService
    {
        private readonly VaultService _vaultService;
        private readonly ILogger<CredentialRefreshService> _logger;
        private readonly IOptionsMonitor<DatabaseCredentials> _dbCredentialsMonitor;
        private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);

        public CredentialRefreshService(
            VaultService vaultService,
            ILogger<CredentialRefreshService> logger,
            IOptionsMonitor<DatabaseCredentials> dbCredentialsMonitor)
        {
            _vaultService = vaultService;
            _logger = logger;
            _dbCredentialsMonitor = dbCredentialsMonitor;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Refreshing database credentials from Vault.");

                    var newCredentials = await _vaultService.FetchPostgresCredentialsAsync("vault");

                    // Update the DatabaseCredentials options
                    _dbCredentialsMonitor.CurrentValue.Username = newCredentials.Username;
                    _dbCredentialsMonitor.CurrentValue.Password = newCredentials.Password;

                    _logger.LogInformation("Database credentials refreshed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error refreshing database credentials.");
                }

                // Wait until next refresh interval
                await Task.Delay(_refreshInterval, stoppingToken);
            }
        }
    }
}
