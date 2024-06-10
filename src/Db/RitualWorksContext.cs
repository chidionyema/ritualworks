using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Collections.Generic;
using System.Linq;


namespace RitualWorks.Db
{
    public class RitualWorksContext : IdentityDbContext<User>
    {
        public DbSet<Ritual> Rituals { get; set; }
        public DbSet<RitualFTS> RitualFTS { get; set; }
        public DbSet<Petition> Petitions { get; set; }
        public DbSet<Donation> Donations { get; set; }
        public DbSet<Post> Posts { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        public RitualWorksContext(DbContextOptions<RitualWorksContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure entity relationships and constraints

            modelBuilder.Entity<Product>()
                 .HasMany(p => p.ProductImages)
                 .WithOne(pi => pi.Product)
                 .HasForeignKey(pi => pi.ProductId);

            modelBuilder.Entity<Category>()
                .HasMany(c => c.Products)
                .WithOne(p => p.Category)
                .HasForeignKey(p => p.CategoryId);

            modelBuilder.Entity<Order>()
               .HasMany(o => o.OrderItems)
               .WithOne(oi => oi.Order)
               .HasForeignKey(oi => oi.OrderId);

            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Product)
                .WithMany()
                .HasForeignKey(oi => oi.ProductId);

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

            modelBuilder.Entity<Post>()
             .HasMany(p => p.Comments)
             .WithOne(c => c.Post)
             .HasForeignKey(c => c.PostId);

            modelBuilder.Entity<RitualFTS>()
               .ToTable("RitualsFTS")
               .HasKey(r => r.Id);

        }

        public override int SaveChanges()
        {
            var entities = ChangeTracker.Entries()
                .Where(e => e.Entity is Ritual && (e.State == EntityState.Added || e.State == EntityState.Modified))
                .Select(e => e.Entity as Ritual);

            var result = base.SaveChanges();

            foreach (var entity in entities)
            {
                Database.ExecuteSqlRaw(
                    "INSERT OR REPLACE INTO RitualsFTS (Id, Title, Description, FullContent) VALUES ({0}, {1}, {2}, {3})",
                    entity.Id, entity.Title, entity.Description, entity.FullTextContent);
            }

            return result;
        }
    }
}
