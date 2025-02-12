using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace haworks.Db
{
    public class haworksContextFactory : IDesignTimeDbContextFactory<haworksContext>
    {
        public haworksContext CreateDbContext(string[] args)
        {
            // Build configuration from the current directory.
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            // Determine the current environment.
            string env = config["ASPNETCORE_ENVIRONMENT"] ?? "Development";
            string connectionString = string.Empty;

            if (env.Equals("Development", StringComparison.OrdinalIgnoreCase))
            {
                // For local development (and migrations), use the default connection string.
                connectionString = config.GetConnectionString("DefaultConnection");
                Console.WriteLine($"[Debug] Environment is '{env}'. Using default connection string from configuration.");
            }
            else
            {
                // In production (or non‑Development), attempt to retrieve the connection string via Vault.
                // Check for the existence of the vault files.
                var roleIdPath = config["Vault:RoleIdPath"];
                var secretIdPath = config["Vault:SecretIdPath"];

                if (!string.IsNullOrEmpty(roleIdPath) && File.Exists(roleIdPath) &&
                    !string.IsNullOrEmpty(secretIdPath) && File.Exists(secretIdPath))
                {
                    try
                    {
                        // Initialize your VaultService (assumed to be implemented elsewhere).
                        var vaultService = new Haworks.Services.VaultService(config);
                        connectionString = vaultService.GetDatabaseConnectionString().Result;
                        Console.WriteLine("[Debug] Retrieved connection string from Vault.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] Failed to retrieve connection string from Vault: {ex.Message}");
                        // Fall back to default connection string.
                        connectionString = config.GetConnectionString("DefaultConnection");
                        Console.WriteLine("[Debug] Using default connection string from configuration as fallback.");
                    }
                }
                else
                {
                    // Fallback if vault files are missing.
                    connectionString = config.GetConnectionString("DefaultConnection");
                    Console.WriteLine("[Debug] Vault files not found. Using default connection string from configuration.");
                }
            }

            // Configure the DbContext options.
            var optionsBuilder = new DbContextOptionsBuilder<haworksContext>();
            optionsBuilder
                .UseNpgsql(connectionString)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .EnableDetailedErrors();

            return new haworksContext(optionsBuilder.Options);
        }
    }
}
