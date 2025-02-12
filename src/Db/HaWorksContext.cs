using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Haworks.Services;

namespace haworks.Db
{
    public class haworksContext : IdentityDbContext<User>
    {
        // Existing DbSets
        public DbSet<Product> Products { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<ProductReview> ProductReviews { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Content> Contents { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<ProductMetadata> ProductMetadata { get; set; }
        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<WebhookEvent> WebhookEvents { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; } // Add this line

        public haworksContext(DbContextOptions<haworksContext> options)
            : base(options)
        {
            // Optimize queries by default using NoTracking
            this.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            // Logs SQL to Console (useful for debugging)
            optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureRowVersion(modelBuilder);
            ConfigureRelationships(modelBuilder);
            SeedData(modelBuilder);

             // Configure relationship for RefreshToken if needed.
             // Assuming RefreshToken entity already exists and has UserId property
             modelBuilder.Entity<RefreshToken>(entity =>
             {
                 entity.HasKey(rt => rt.Id); // Assuming RefreshToken has an Id property

                 entity.HasOne(rt => rt.User) // Navigation to User
                       .WithMany() // User can have many RefreshTokens (or adjust as needed)
                       .HasForeignKey(rt => rt.UserId)
                       .OnDelete(DeleteBehavior.Cascade); // Cascade delete refresh tokens if user is deleted
             });
        }

        private void ConfigureRowVersion(ModelBuilder modelBuilder)
        {
            /*
            // Example for concurrency row version (if needed):
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
            */
        }

        private void ConfigureRelationships(ModelBuilder modelBuilder)
        {
             modelBuilder.Entity<UserProfile>(entity =>
                {
                    entity.HasKey(up => up.Id);

                    // one-to-one or one-to-many,
                    // typically one-to-one if each user has exactly one profile
                    entity.HasOne(up => up.User)
                        .WithOne() // or WithMany() if one user can have multiple profiles
                        .HasForeignKey<UserProfile>(up => up.UserId)
                        .OnDelete(DeleteBehavior.Cascade);
                });
            // 1) Product -> ProductReview (one-to-many)
            modelBuilder.Entity<Product>()
                .HasMany(p => p.ProductReviews)
                .WithOne(pr => pr.Product)
                .HasForeignKey(pr => pr.ProductId);

            // 2) Category -> Product (one-to-many)
            modelBuilder.Entity<Category>()
                .HasMany(c => c.Products)
                .WithOne(p => p.Category)
                .HasForeignKey(p => p.CategoryId);

            // 3) Order -> OrderItems (one-to-many), plus Payment
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasMany(o => o.OrderItems)
                      .WithOne(oi => oi.Order)
                      .HasForeignKey(oi => oi.OrderId);

                entity.Property(o => o.Status).HasConversion<string>();
                entity.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");

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

            // 4) Content
            modelBuilder.Entity<Content>(entity =>
            {
                entity.Property(c => c.ContentType).HasConversion<string>();
                entity.HasIndex(c => new { c.EntityId, c.EntityType })
                      .HasDatabaseName("IX_EntityContent");
            });

            // 5) Payment
            modelBuilder.Entity<Payment>(entity =>
            {
                entity.HasOne(p => p.Order)
                      .WithOne(o => o.Payment)
                      .HasForeignKey<Payment>(p => p.OrderId);

                entity.Property(p => p.Status).HasConversion<string>();
                entity.Property(p => p.Amount).HasColumnType("decimal(18,2)");
                entity.Property(p => p.Tax).HasColumnType("decimal(18,2)");
            });

            // 6) NEW: Product -> ProductMetadata (one-to-many)
            modelBuilder.Entity<ProductMetadata>()
                .HasOne(pm => pm.Product)
                .WithMany(p => p.Metadata) // Navigation property in Product
                .HasForeignKey(pm => pm.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        private void SeedData(ModelBuilder modelBuilder)
        {
            var categoryIds = new[]
            {
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333")
            };

            var createdAt = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var products = new List<Product>();
            var contents = new List<Content>();
            var random = new Random();

            // Create 200 products with random data
            for (int i = 0; i < 200; i++)
            {
                var productId = Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}");
                var categoryId = categoryIds[random.Next(categoryIds.Length)];

                products.Add(new Product
                {
                    Id = productId,
                    Name = $"Product {i + 1}",
                    Headline = $"Exciting Product {i + 1}",
                    Title = $"Detailed Title for Product {i + 1}",
                    ShortDescription = $"Brief overview of Product {i + 1}",
                    Description = $"This is the detailed description for Product {i + 1}. It comes with amazing features.",
                    UnitPrice = random.Next(10, 500) + 0.99m,
                    Rating = Math.Round(random.NextDouble() * 5, 1),
                    IsListed = random.Next(2) == 1,
                    IsFeatured = random.Next(2) == 1,
                    Stock = random.Next(50, 500),
                    IsInStock = random.Next(2) == 1,
                    Brand = $"Brand {random.Next(1, 10)}",
                    Type = random.Next(2) == 1 ? "physical" : "digital",
                    CategoryId = categoryId
                });

                // 3 images per product
                for (int j = 0; j < 3; j++)
                {
                    contents.Add(new Content
                    {
                        Id = Guid.Parse($"10000000-0000-0000-0000-{(i * 3 + j + 1):D12}"),
                        EntityId = productId,
                        EntityType = "Product",
                        ContentType = ContentType.Image,
                        Url = $"https://via.placeholder.com/300?text=Product+{i + 1}+Image+{j + 1}",
                        BlobName = $"product{i + 1}-image{j + 1}.jpg",
                        CreatedAt = createdAt
                    });
                }
            }

            // Seed Categories
            modelBuilder.Entity<Category>().HasData(
                new Category { Id = categoryIds[0], Name = "Electronics" },
                new Category { Id = categoryIds[1], Name = "Apparel" },
                new Category { Id = categoryIds[2], Name = "Home" }
            );

            // Seed Products
            modelBuilder.Entity<Product>().HasData(products);

            // Seed Contents
            modelBuilder.Entity<Content>().HasData(contents);

            // --------------------------------------
            // SEED ProductMetadata
            // --------------------------------------
            var allMetadata = new List<ProductMetadata>();

            foreach (var product in products)
            {
                // 1) CourseInfo
                // You can store any relevant fields: level, duration, rating, price, etc.
                var courseInfoJson =
                    @"{
                        ""level"": ""Intermediate"",
                        ""duration"": ""5h 12m"",
                        ""rating"": 4.8,
                        ""price"": 49.99
                    }";

                allMetadata.Add(new ProductMetadata
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    KeyName = "CourseInfo",
                    KeyValue = courseInfoJson
                });

                // 2) CourseCurriculum (array of lessons)
                var curriculumJson =
                    @"[
                        { ""title"": ""Introduction"", ""duration"": ""2:00"", ""description"": ""Learn the basics"" },
                        { ""title"": ""Advanced Topics"", ""duration"": ""5:00"", ""description"": ""Deep dive"" }
                    ]";

                allMetadata.Add(new ProductMetadata
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    KeyName = "CourseCurriculum",
                    KeyValue = curriculumJson
                });

                // 3) AuthorInfo
                var authorInfoJson =
                    @"{
                        ""name"": ""John Doe"",
                        ""avatar"": ""/avatar-placeholder.png"",
                        ""bio"": ""John Doe is a highly experienced software engineer, author, and educator."",
                        ""website"": ""https://johndoe.com""
                    }";

                allMetadata.Add(new ProductMetadata
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    KeyName = "AuthorInfo",
                    KeyValue = authorInfoJson
                });
            }

            modelBuilder.Entity<ProductMetadata>().HasData(allMetadata);
        }


        // SaveChanges overrides to set CreatedAt/LastModifiedDate
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