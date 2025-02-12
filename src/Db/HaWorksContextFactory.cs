using haworks.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Haworks.Services;

namespace haworks.Db
{
    public class haworksContextFactory : IDesignTimeDbContextFactory<haworksContext>
    {
        private readonly IConnectionStringProvider _connectionStringProvider;

        // Constructor for runtime DI
        public haworksContextFactory(IConnectionStringProvider connectionStringProvider)
        {
            _connectionStringProvider = connectionStringProvider;
        }

        // Fallback constructor for design-time operations
        public haworksContextFactory() { }

        public haworksContext CreateDbContext(string[] args)
        {
            // Initialize configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            // Initialize VaultService
            var vaultService = new VaultService(config);

            // Retrieve the database connection string from Vault
            string connectionString = vaultService.GetDatabaseConnectionString().Result;

            Console.WriteLine($"[Debug] Connection string retrieved from Vault: {connectionString}");

            // Create DbContextOptions
            var optionsBuilder = new DbContextOptionsBuilder<haworksContext>();
            optionsBuilder
                .UseNpgsql(connectionString)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .EnableDetailedErrors();

            // Create and return the context
            return new haworksContext(optionsBuilder.Options);
        }
    }
}
