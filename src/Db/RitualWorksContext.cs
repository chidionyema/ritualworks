using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RitualWorks.Db
{
    public class RitualWorksContext : IdentityDbContext<User>
    {
        public DbSet<Ritual> Rituals { get; set; }
        public DbSet<Post> Posts { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<ProductAsset> ProductAssets { get; set; }
        public DbSet<ProductReview> ProductReviews { get; set; }
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

            modelBuilder.Entity<Product>()
                .HasMany(p => p.ProductAssets)
                .WithOne(pi => pi.Product)
                .HasForeignKey(pi => pi.ProductId);

            modelBuilder.Entity<Product>()
                .HasMany(p => p.ProductReviews)
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

            modelBuilder.Entity<User>()
                .HasMany(u => u.Rituals)
                .WithOne(r => r.Creator)
                .HasForeignKey(r => r.CreatorId);

            // Configure RitualType enum to be stored as string
            modelBuilder.Entity<Ritual>()
                .Property(r => r.RitualType)
                .HasConversion<string>();

            modelBuilder.Entity<Post>()
                .HasMany(p => p.Comments)
                .WithOne(c => c.Post)
                .HasForeignKey(c => c.PostId);

            // Seed initial categories and products with explicit, valid GUIDs
            var category1Id = Guid.NewGuid();
            var category2Id = Guid.NewGuid();
            var category3Id = Guid.NewGuid();

            SeedCategories(modelBuilder, category1Id, category2Id, category3Id);
            SeedProducts(modelBuilder, category1Id, category2Id, category3Id);
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

        private void SeedProducts(ModelBuilder modelBuilder, Guid category1Id, Guid category2Id, Guid category3Id)
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

            var productImages = new List<ProductImage>
            {
                new ProductImage { Id = Guid.NewGuid(), Url = "https://via.placeholder.com/300", ProductId = product1Id },
                new ProductImage { Id = Guid.NewGuid(), Url = "https://via.placeholder.com/300", ProductId = product2Id },
                new ProductImage { Id = Guid.NewGuid(), Url = "https://via.placeholder.com/300", ProductId = product3Id }
            };

            modelBuilder.Entity<ProductImage>().HasData(productImages);

            var productReviews = new List<ProductReview>
            {
                new ProductReview { Id = Guid.NewGuid(), User = "Alice", Comment = "Great product!", Rating = 5, ProductId = product1Id },
                new ProductReview { Id = Guid.NewGuid(), User = "Bob", Comment = "Good value for money.", Rating = 4, ProductId = product1Id },
                new ProductReview { Id = Guid.NewGuid(), User = "Charlie", Comment = "Excellent quality!", Rating = 5, ProductId = product2Id },
                new ProductReview { Id = Guid.NewGuid(), User = "Dana", Comment = "Satisfactory.", Rating = 3, ProductId = product2Id },
                new ProductReview { Id = Guid.NewGuid(), User = "Eve", Comment = "Could be better.", Rating = 3, ProductId = product3Id },
                new ProductReview { Id = Guid.NewGuid(), User = "Frank", Comment = "Not worth the price.", Rating = 2, ProductId = product3Id }
            };

            modelBuilder.Entity<ProductReview>().HasData(productReviews);
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
