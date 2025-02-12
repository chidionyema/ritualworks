using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Haworks.Services
{
    public class VaultHealthCheck : IHealthCheck
    {
        private readonly VaultService _vault;

        public VaultHealthCheck(VaultService vault) => _vault = vault;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var creds = await _vault.GetDatabaseConnectionString();
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Vault connection failed", ex);
            }
        }
    }
}
