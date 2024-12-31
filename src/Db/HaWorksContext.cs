using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace haworks.Db
{
    public class haworksContext : IdentityDbContext<User>
    {
        public DbSet<Product> Products { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<ProductReview> ProductReviews { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Content> Contents { get; set; }
        public DbSet<Payment> Payments { get; set; } // Add Payment DbSet

        public haworksContext(DbContextOptions<haworksContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureRowVersion(modelBuilder);
            ConfigureRelationships(modelBuilder);
            SeedData(modelBuilder);
        }

        private void ConfigureRowVersion(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(AuditableEntity).IsAssignableFrom(entityType.ClrType))
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(nameof(AuditableEntity.RowVersion))
                        .IsRequired()
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate();
                }
            }
        }

        private void ConfigureRelationships(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>()
                .HasMany(p => p.ProductReviews)
                .WithOne(pr => pr.Product)
                .HasForeignKey(pr => pr.ProductId);

            modelBuilder.Entity<Category>()
                .HasMany(c => c.Products)
                .WithOne(p => p.Category)
                .HasForeignKey(p => p.CategoryId);

            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasMany(o => o.OrderItems)
                    .WithOne(oi => oi.Order)
                    .HasForeignKey(oi => oi.OrderId);

                entity.Property(o => o.Status)
                    .HasConversion<string>();

                entity.Property(o => o.TotalAmount)
                    .HasColumnType("decimal(18,2)");

                entity.HasOne(o => o.Payment)
                    .WithOne(p => p.Order)
                    .HasForeignKey<Payment>(p => p.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.HasOne(oi => oi.Product)
                    .WithMany()
                    .HasForeignKey(oi => oi.ProductId);
            });

            modelBuilder.Entity<Content>(entity =>
            {
                entity.Property(c => c.ContentType).HasConversion<string>();
                entity.HasIndex(c => new { c.EntityId, c.EntityType }).HasDatabaseName("IX_EntityContent");
            });

            modelBuilder.Entity<Payment>(entity =>
            {
                entity.HasOne(p => p.Order)
                    .WithOne(o => o.Payment)
                    .HasForeignKey<Payment>(p => p.OrderId);

                entity.Property(p => p.Status)
                    .HasConversion<string>();

                entity.Property(p => p.Amount)
                    .HasColumnType("decimal(18,2)");

                entity.Property(p => p.Tax)
                    .HasColumnType("decimal(18,2)");
            });
        }

        private void SeedData(ModelBuilder modelBuilder)
        {
            var category1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var category2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var category3Id = Guid.Parse("33333333-3333-3333-3333-333333333333");

            var product1Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var product2Id = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var product3Id = Guid.Parse("66666666-6666-6666-6666-666666666666");

            var content1Id = Guid.Parse("77777777-7777-7777-7777-777777777777");
            var content2Id = Guid.Parse("88888888-8888-8888-8888-888888888888");

            modelBuilder.Entity<Category>().HasData(
                new Category { Id = category1Id, Name = "Electronics", RowVersion = 1 },
                new Category { Id = category2Id, Name = "Apparel", RowVersion = 1 },
                new Category { Id = category3Id, Name = "Home", RowVersion = 1 }
            );

            modelBuilder.Entity<Product>().HasData(
                new Product
                {
                    Id = product1Id,
                    Name = "Product 1",
                    Description = "Description of Product 1",
                    UnitPrice = 19.99m,
                    Stock = 100,
                    Rating = 4.5,
                    IsListed = true,
                    CategoryId = category1Id,
                    Brand = "Brand A",
                    Type = "physical",
                    IsInStock = true,
                    RowVersion = 1
                },
                new Product
                {
                    Id = product2Id,
                    Name = "Product 2",
                    Description = "Description of Product 2",
                    UnitPrice = 29.99m,
                    Stock = 150,
                    Rating = 4.0,
                    IsListed = false,
                    CategoryId = category2Id,
                    Brand = "Brand B",
                    Type = "digital",
                    IsInStock = true,
                    RowVersion = 1
                },
                new Product
                {
                    Id = product3Id,
                    Name = "Product 3",
                    Description = "Description of Product 3",
                    UnitPrice = 39.99m,
                    Stock = 200,
                    Rating = 3.5,
                    IsListed = true,
                    CategoryId = category3Id,
                    Brand = "Brand C",
                    Type = "physical",
                    IsInStock = false,
                    RowVersion = 1
                }
            );

            modelBuilder.Entity<Content>().HasData(
                new Content
                {
                    Id = content1Id,
                    EntityId = product1Id,
                    EntityType = "Product",
                    ContentType = ContentType.Image,
                    Url = "https://via.placeholder.com/300",
                    BlobName = "product1-image1.jpg",
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2024, 1, 1, 12, 0, 0), DateTimeKind.Utc),
                    RowVersion = 1
                },
                new Content
                {
                    Id = content2Id,
                    EntityId = product2Id,
                    EntityType = "Product",
                    ContentType = ContentType.Image,
                    Url = "https://via.placeholder.com/300",
                    BlobName = "product2-image1.jpg",
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2024, 1, 1, 12, 0, 0), DateTimeKind.Utc),
                    RowVersion = 1
                }
            );
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedAt = DateTime.SpecifyKind(new DateTime(2024, 1, 1, 12, 0, 0), DateTimeKind.Utc);
                    entry.Entity.CreatedBy = "System";
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.LastModifiedDate = DateTime.SpecifyKind(new DateTime(2024, 1, 1, 12, 0, 0), DateTimeKind.Utc);
                    entry.Entity.LastModifiedBy = "System";
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedAt = DateTime.SpecifyKind(new DateTime(2024, 1, 1, 12, 0, 0), DateTimeKind.Utc);
                    entry.Entity.CreatedBy = "System";
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.LastModifiedDate = DateTime.SpecifyKind(new DateTime(2024, 1, 1, 12, 0, 0), DateTimeKind.Utc);
                    entry.Entity.LastModifiedBy = "System";
                }
            }

            return base.SaveChanges();
        }
    }
}
