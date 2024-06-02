using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RitualWorks.Db
{
    public class RitualWorksContext : IdentityDbContext<User>
    {
        public DbSet<Ritual> Rituals { get; set; }
        public DbSet<Petition> Petitions { get; set; }
        public DbSet<Donation> Donations { get; set; }

        public RitualWorksContext(DbContextOptions<RitualWorksContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure entity relationships and constraints
            modelBuilder.Entity<Ritual>()
                .HasMany(r => r.Petitions)
                .WithOne(p => p.Ritual)
                .HasForeignKey(p => p.RitualId);

            modelBuilder.Entity<Ritual>()
                .HasMany(r => r.Donations)
                .WithOne(d => d.Ritual)
                .HasForeignKey(d => d.RitualId);

            modelBuilder.Entity<User>()
                .HasMany(u => u.Rituals)
                .WithOne(r => r.Creator)
                .HasForeignKey(r => r.CreatorId);

            modelBuilder.Entity<User>()
                .HasMany(u => u.Petitions)
                .WithOne(p => p.User)
                .HasForeignKey(p => p.UserId);

            modelBuilder.Entity<User>()
                .HasMany(u => u.Donations)
                .WithOne(d => d.User)
                .HasForeignKey(d => d.UserId);

            // Configure RitualType enum to be stored as string
            modelBuilder.Entity<Ritual>()
                .Property(r => r.RitualType)
                .HasConversion<string>();
        }
    }
}
