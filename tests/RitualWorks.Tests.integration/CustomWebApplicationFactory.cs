using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RitualWorks.Db;

namespace RitualWorks.Tests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<RitualWorksContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<RitualWorksContext>(options =>
                {
                    options.UseSqlite("Filename=:memory:");
                });

                var sp = services.BuildServiceProvider();

                using (var scope = sp.CreateScope())
                {
                    var scopedServices = scope.ServiceProvider;
                    var db = scopedServices.GetRequiredService<RitualWorksContext>();
                    db.Database.OpenConnection();
                    db.Database.EnsureCreated();

                    // Seed the database with test data.
                    SeedDatabase(db);
                }
            });
        }

        private void SeedDatabase(RitualWorksContext context)
        {
            // Add seed data here.
        }
    }
}
