using System;
using System.IO;
using System.Text.Json;
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
        private readonly ILogger<CredentialRefreshService> _logger;
        private readonly IOptionsMonitor<DatabaseCredentials> _dbCredentialsMonitor;
        private readonly string _dbCredsFilePath = "/vault/secrets/db-creds.json"; // Path to the Vault Agent template output
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

        public CredentialRefreshService(
            ILogger<CredentialRefreshService> logger,
            IOptionsMonitor<DatabaseCredentials> dbCredentialsMonitor)
        {
            _logger = logger;
            _dbCredentialsMonitor = dbCredentialsMonitor;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(_dbCredsFilePath))
                    {
                        _logger.LogInformation("Reading latest credentials from db-creds.json.");

                        var credsContent = await File.ReadAllTextAsync(_dbCredsFilePath, stoppingToken);
                        var newCredentials = JsonSerializer.Deserialize<DatabaseCredentials>(credsContent);

                        if (newCredentials != null)
                        {
                            // Update the DatabaseCredentials options
                            _dbCredentialsMonitor.CurrentValue.Username = newCredentials.Username;
                            _dbCredentialsMonitor.CurrentValue.Password = newCredentials.Password;

                            _logger.LogInformation("Database credentials refreshed from db-creds.json.");
                        }
                        else
                        {
                            _logger.LogWarning("Failed to deserialize credentials from db-creds.json.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Credentials file {_dbCredsFilePath} not found.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading credentials from db-creds.json.");
                }

                // Wait until next check interval
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }
    }
}
