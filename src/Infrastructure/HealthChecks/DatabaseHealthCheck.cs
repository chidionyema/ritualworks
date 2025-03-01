using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Haworks.Infrastructure.Data; // Ensure correct namespace
using haworks.Db;

namespace Haworks.Infrastructure
{
    public class DatabaseHealthCheck : IHealthCheck
    {
        private readonly ProductContext _productContext;
        private readonly OrderContext _orderContext;
        private readonly IdentityContext _identityContext;
        private readonly ContentContext _contentContext;  // Added ContentContext
        private readonly ILogger<DatabaseHealthCheck> _logger;

        public DatabaseHealthCheck(
            ProductContext productContext,
            OrderContext orderContext,
            IdentityContext identityContext,
            ContentContext contentContext, // Added ContentContext
            ILogger<DatabaseHealthCheck> logger)
        {
            _productContext = productContext ?? throw new ArgumentNullException(nameof(productContext));
            _orderContext = orderContext ?? throw new ArgumentNullException(nameof(orderContext));
            _identityContext = identityContext ?? throw new ArgumentNullException(nameof(identityContext));
            _contentContext = contentContext ?? throw new ArgumentNullException(nameof(contentContext)); // Initialized ContentContext
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var data = new Dictionary<string, object>
            {
                ["ProductDb"] = await GetDatabaseStatus(_productContext, cancellationToken),
                ["OrderDb"] = await GetDatabaseStatus(_orderContext, cancellationToken),
                ["IdentityDb"] = await GetDatabaseStatus(_identityContext, cancellationToken),
                ["ContentDb"] = await GetDatabaseStatus(_contentContext, cancellationToken),  // Checking ContentContext
                ["EntityCounts"] = new Dictionary<string, object>
                {
                    ["Products"] = await _productContext.Products.CountAsync(cancellationToken),
                    ["Orders"] = await _orderContext.Orders.CountAsync(cancellationToken),
                    ["Users"] = await _identityContext.UserProfiles.CountAsync(cancellationToken),
                    ["Content"] = await _contentContext.Contents.CountAsync(cancellationToken)  // Added Content count
                }
            };

            try
            {
                var results = new List<HealthCheckResult>
                {
                    await CheckContextHealth(_productContext, "Products", cancellationToken),
                    await CheckContextHealth(_orderContext, "Orders", cancellationToken),
                    await CheckContextHealth(_identityContext, "Identity", cancellationToken),
                    await CheckContextHealth(_contentContext, "Content", cancellationToken)  // Added Content context check
                };

                if (results.Any(r => r.Status != HealthStatus.Healthy))
                {
                    // No longer passing `exceptions` as it is not a valid parameter in the current version
                    return new HealthCheckResult(
                        context.Registration.FailureStatus,
                        description: "Some databases are unhealthy",
                        data: data);
                }

                return HealthCheckResult.Healthy("All databases operational", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed");
                return HealthCheckResult.Unhealthy("Critical database failure", data: data);
            }
        }


        private async Task<Dictionary<string, object>> GetDatabaseStatus(
            DbContext dbContext, 
            CancellationToken cancellationToken)
        {
            return new Dictionary<string, object>
            {
                ["ConnectionString"] = dbContext.Database.GetDbConnection().ConnectionString,
                ["PendingMigrations"] = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken),
                ["CanConnect"] = await dbContext.Database.CanConnectAsync(cancellationToken)
            };
        }

        private async Task<HealthCheckResult> CheckContextHealth(
            DbContext dbContext,
            string contextName,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!await dbContext.Database.CanConnectAsync(cancellationToken))
                {
                    return HealthCheckResult.Unhealthy($"{contextName} database connection failed");
                }

                await dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
                return HealthCheckResult.Healthy($"{contextName} database healthy");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ContextName} database health check failed", contextName);
                return HealthCheckResult.Unhealthy($"{contextName} database failure", ex);
            }
        }
    }
}
