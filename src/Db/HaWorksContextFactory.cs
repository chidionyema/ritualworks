using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace haworks.Db
{
    public class haworksContextFactory : IDesignTimeDbContextFactory<haworksContext>
    {
        public haworksContext CreateDbContext(string[] args)
        {
            // Build configuration
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            // Get the connection string
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            // Create DbContextOptions
            var optionsBuilder = new DbContextOptionsBuilder<haworksContext>();
            optionsBuilder.UseNpgsql(connectionString);

            // Create and return the context
            return new haworksContext(optionsBuilder.Options);
        }
    }
}