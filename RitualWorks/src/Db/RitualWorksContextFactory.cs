using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace RitualWorks.Db
{
    public class RitualWorksContextFactory : IDesignTimeDbContextFactory<RitualWorksContext>
    {
        public RitualWorksContext CreateDbContext(string[] args)
        {
            // Build configuration
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            // Get the connection string
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            // Create DbContextOptions
            var optionsBuilder = new DbContextOptionsBuilder<RitualWorksContext>();
            optionsBuilder.UseNpgsql(connectionString);

            // Create and return the context
            return new RitualWorksContext(optionsBuilder.Options);
        }
    }
}