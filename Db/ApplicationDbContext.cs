using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RitualWorks.Db
{
    public class ApplicationDbContext : IdentityDbContext<User>
    {
        public DbSet<Ritual> Rituals { get; set; }
        public DbSet<RitualType> RitualTypes { get; set; }
        public DbSet<Intent> Intents { get; set; }
        public DbSet<Donation> Donations { get; set; }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Donation>(entity =>
            {
                entity.Property(e => e.Amount)
                      .HasColumnType("decimal(18, 2)");
            });

            modelBuilder.Entity<RitualType>()
                .HasMany(rt => rt.Rituals)
                .WithOne(r => r.RitualType)
                .HasForeignKey(r => r.RitualTypeId);

            modelBuilder.Entity<Ritual>()
                .HasMany(r => r.Intents)
                .WithOne(i => i.Ritual)
                .HasForeignKey(i => i.RitualId);

            modelBuilder.Entity<Ritual>()
                .HasMany(r => r.Donations)
                .WithOne(d => d.Ritual)
                .HasForeignKey(d => d.RitualId);

            modelBuilder.Entity<User>()
                .HasMany(u => u.Rituals)
                .WithOne(r => r.Creator)
                .HasForeignKey(r => r.CreatorId);

            modelBuilder.Entity<User>()
                .HasMany(u => u.Intents)
                .WithOne(i => i.User)
                .HasForeignKey(i => i.UserId);

            modelBuilder.Entity<User>()
                .HasMany(u => u.Donations)
                .WithOne(d => d.User)
                .HasForeignKey(d => d.UserId);
        }
    }
}

