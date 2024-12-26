using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace haworks.Db
{
    public class haworksContext : IdentityDbContext<User>
    {
        public DbSet<Post> Posts { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<ProductReview> ProductReviews { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Content> Contents { get; set; } // New Content DbSet

        public haworksContext(DbContextOptions<haworksContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure entity relationships and constraints
            modelBuilder.Entity<Product>()
                .HasMany(p => p.ProductReviews)
                .WithOne(pr => pr.Product)
                .HasForeignKey(pr => pr.ProductId);

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

            modelBuilder.Entity<Post>()
                .HasMany(p => p.Comments)
                .WithOne(c => c.Post)
                .HasForeignKey(c => c.PostId);

            // Configure Content entity
            modelBuilder.Entity<Content>(entity =>
            {
                entity.Property(c => c.ContentType).HasConversion<string>(); // Enum stored as string
                entity.HasIndex(c => new { c.EntityId, c.EntityType }).HasDatabaseName("IX_EntityContent");
            });

            // Seed initial categories, products, and content
            var category1Id = Guid.NewGuid();
            var category2Id = Guid.NewGuid();
            var category3Id = Guid.NewGuid();

            SeedCategories(modelBuilder, category1Id, category2Id, category3Id);
            SeedProductsAndContent(modelBuilder, category1Id, category2Id, category3Id);
        }

        private void SeedCategories(ModelBuilder modelBuilder, Guid category1Id, Guid category2Id, Guid category3Id)
        {
            var categories = new List<Category>
            {
                new Category(category1Id, "Electronics"),
                new Category(category2Id, "Apparel"),
                new Category(category3Id, "Home")
            };

            modelBuilder.Entity<Category>().HasData(categories);
        }

        private void SeedProductsAndContent(ModelBuilder modelBuilder, Guid category1Id, Guid category2Id, Guid category3Id)
        {
            var product1Id = Guid.NewGuid();
            var product2Id = Guid.NewGuid();
            var product3Id = Guid.NewGuid();

            var products = new List<Product>
            {
                new Product
                {
                    Id = product1Id,
                    Name = "Product 1",
                    Description = "Description of Product 1",
                    Price = 19.99m,
                    Stock = 100,
                    Rating = 4.5,
                    IsNew = true,
                    CategoryId = category1Id,
                    Brand = "Brand A",
                    Type = "physical",
                    InStock = true
                },
                new Product
                {
                    Id = product2Id,
                    Name = "Product 2",
                    Description = "Description of Product 2",
                    Price = 29.99m,
                    Stock = 150,
                    Rating = 4.0,
                    IsNew = false,
                    CategoryId = category2Id,
                    Brand = "Brand B",
                    Type = "digital",
                    InStock = true
                },
                new Product
                {
                    Id = product3Id,
                    Name = "Product 3",
                    Description = "Description of Product 3",
                    Price = 39.99m,
                    Stock = 200,
                    Rating = 3.5,
                    IsNew = true,
                    CategoryId = category3Id,
                    Brand = "Brand C",
                    Type = "physical",
                    InStock = false
                }
            };

            modelBuilder.Entity<Product>().HasData(products);

            // Seed content data
            modelBuilder.Entity<Content>().HasData(new List<Content>
            {
                new Content
                {
                    Id = Guid.NewGuid(),
                    EntityId = product1Id,
                    EntityType = "Product",
                    ContentType = ContentType.Image,
                    Url = "https://via.placeholder.com/300",
                    BlobName = "product1-image1.jpg",
                    CreatedAt = DateTime.UtcNow
                },
                new Content
                {
                    Id = Guid.NewGuid(),
                    EntityId = product1Id,
                    EntityType = "Product",
                    ContentType = ContentType.Asset,
                    Url = "https://via.placeholder.com/asset1",
                    BlobName = "product1-asset1.pdf",
                    CreatedAt = DateTime.UtcNow
                },
                new Content
                {
                    Id = Guid.NewGuid(),
                    EntityId = product2Id,
                    EntityType = "Product",
                    ContentType = ContentType.Image,
                    Url = "https://via.placeholder.com/300",
                    BlobName = "product2-image1.jpg",
                    CreatedAt = DateTime.UtcNow
                }
            });
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedDate = DateTime.UtcNow;
                    entry.Entity.CreatedBy = ""; // Provide a suitable default value
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.LastModifiedDate = DateTime.UtcNow;
                    entry.Entity.LastModifiedBy = ""; // Provide a suitable default value
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            var result = base.SaveChanges();
            return result;
        }
    }
}
