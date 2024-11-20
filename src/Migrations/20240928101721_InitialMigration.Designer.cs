﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using haworks.Db;

#nullable disable

namespace haworks.Migrations
{
    [DbContext(typeof(haworksContext))]
    [Migration("20240928101721_InitialMigration")]
    partial class InitialMigration
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.6")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRole", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken()
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("NormalizedName")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.HasKey("Id");

                    b.HasIndex("NormalizedName")
                        .IsUnique()
                        .HasDatabaseName("RoleNameIndex");

                    b.ToTable("AspNetRoles", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<string>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("ClaimType")
                        .HasColumnType("text");

                    b.Property<string>("ClaimValue")
                        .HasColumnType("text");

                    b.Property<string>("RoleId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetRoleClaims", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<string>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("ClaimType")
                        .HasColumnType("text");

                    b.Property<string>("ClaimValue")
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserClaims", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<string>", b =>
                {
                    b.Property<string>("LoginProvider")
                        .HasColumnType("text");

                    b.Property<string>("ProviderKey")
                        .HasColumnType("text");

                    b.Property<string>("ProviderDisplayName")
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("LoginProvider", "ProviderKey");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserLogins", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<string>", b =>
                {
                    b.Property<string>("UserId")
                        .HasColumnType("text");

                    b.Property<string>("RoleId")
                        .HasColumnType("text");

                    b.HasKey("UserId", "RoleId");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetUserRoles", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<string>", b =>
                {
                    b.Property<string>("UserId")
                        .HasColumnType("text");

                    b.Property<string>("LoginProvider")
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.Property<string>("Value")
                        .HasColumnType("text");

                    b.HasKey("UserId", "LoginProvider", "Name");

                    b.ToTable("AspNetUserTokens", (string)null);
                });

            modelBuilder.Entity("haworks.Db.Category", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("CreatedBy")
                        .HasColumnType("text");

                    b.Property<DateTime>("CreatedDate")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("LastModifiedBy")
                        .HasColumnType("text");

                    b.Property<DateTime?>("LastModifiedDate")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("Categories");

                    b.HasData(
                        new
                        {
                            Id = new Guid("47bbbbe2-2a22-4741-a563-15b189936a97"),
                            CreatedDate = new DateTime(2024, 9, 28, 10, 17, 20, 985, DateTimeKind.Utc).AddTicks(3180),
                            Name = "Electronics"
                        },
                        new
                        {
                            Id = new Guid("6584b8b9-e26e-4d2f-83ff-5bc56c27ecd9"),
                            CreatedDate = new DateTime(2024, 9, 28, 10, 17, 20, 985, DateTimeKind.Utc).AddTicks(3190),
                            Name = "Apparel"
                        },
                        new
                        {
                            Id = new Guid("b494b077-e7f4-45a4-8154-50c963b690bc"),
                            CreatedDate = new DateTime(2024, 9, 28, 10, 17, 20, 985, DateTimeKind.Utc).AddTicks(3190),
                            Name = "Home"
                        });
                });

            modelBuilder.Entity("haworks.Db.Comment", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Author")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Content")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("DateCreated")
                        .HasColumnType("timestamp with time zone");

                    b.Property<Guid>("PostId")
                        .HasColumnType("uuid");

                    b.Property<string>("UserId")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("PostId");

                    b.HasIndex("UserId");

                    b.ToTable("Comments");
                });

            modelBuilder.Entity("haworks.Db.Order", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<DateTime>("OrderDate")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Status")
                        .HasColumnType("integer");

                    b.Property<decimal>("TotalAmount")
                        .HasColumnType("numeric");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("Orders");
                });

            modelBuilder.Entity("haworks.Db.OrderItem", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("OrderId")
                        .HasColumnType("uuid");

                    b.Property<decimal>("Price")
                        .HasColumnType("numeric");

                    b.Property<Guid>("ProductId")
                        .HasColumnType("uuid");

                    b.Property<int>("Quantity")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("OrderId");

                    b.HasIndex("ProductId");

                    b.ToTable("OrderItems");
                });

            modelBuilder.Entity("haworks.Db.Post", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Author")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Content")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("DateCreated")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("Posts");
                });

            modelBuilder.Entity("haworks.Db.Product", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("BlobName")
                        .HasColumnType("text");

                    b.Property<string>("Brand")
                        .HasColumnType("text");

                    b.Property<Guid>("CategoryId")
                        .HasColumnType("uuid");

                    b.Property<string>("CreatedBy")
                        .HasColumnType("text");

                    b.Property<DateTime>("CreatedDate")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<bool>("InStock")
                        .HasColumnType("boolean");

                    b.Property<bool>("IsNew")
                        .HasColumnType("boolean");

                    b.Property<string>("LastModifiedBy")
                        .HasColumnType("text");

                    b.Property<DateTime?>("LastModifiedDate")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<decimal>("Price")
                        .HasColumnType("numeric");

                    b.Property<double>("Rating")
                        .HasColumnType("double precision");

                    b.Property<int>("Stock")
                        .HasColumnType("integer");

                    b.Property<string>("Type")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("CategoryId");

                    b.ToTable("Products");

                    b.HasData(
                        new
                        {
                            Id = new Guid("490ee5e9-bf99-42d3-89a3-d3750f85d3f4"),
                            Brand = "Brand A",
                            CategoryId = new Guid("47bbbbe2-2a22-4741-a563-15b189936a97"),
                            CreatedDate = new DateTime(2024, 9, 28, 10, 17, 20, 985, DateTimeKind.Utc).AddTicks(3320),
                            Description = "Description of Product 1",
                            InStock = true,
                            IsNew = true,
                            Name = "Product 1",
                            Price = 19.99m,
                            Rating = 4.5,
                            Stock = 100,
                            Type = "physical"
                        },
                        new
                        {
                            Id = new Guid("8760cf83-9d00-4d1c-bbe6-372b40842b26"),
                            Brand = "Brand B",
                            CategoryId = new Guid("6584b8b9-e26e-4d2f-83ff-5bc56c27ecd9"),
                            CreatedDate = new DateTime(2024, 9, 28, 10, 17, 20, 985, DateTimeKind.Utc).AddTicks(3330),
                            Description = "Description of Product 2",
                            InStock = true,
                            IsNew = false,
                            Name = "Product 2",
                            Price = 29.99m,
                            Rating = 4.0,
                            Stock = 150,
                            Type = "digital"
                        },
                        new
                        {
                            Id = new Guid("8eff2554-c5a7-448e-a4bc-d72ef21c5f4d"),
                            Brand = "Brand C",
                            CategoryId = new Guid("b494b077-e7f4-45a4-8154-50c963b690bc"),
                            CreatedDate = new DateTime(2024, 9, 28, 10, 17, 20, 985, DateTimeKind.Utc).AddTicks(3340),
                            Description = "Description of Product 3",
                            InStock = false,
                            IsNew = true,
                            Name = "Product 3",
                            Price = 39.99m,
                            Rating = 3.5,
                            Stock = 200,
                            Type = "physical"
                        });
                });

            modelBuilder.Entity("haworks.Db.ProductAsset", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("AssetUrl")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("BlobName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<Guid>("ProductId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("ProductId");

                    b.ToTable("ProductAssets");
                });

            modelBuilder.Entity("haworks.Db.ProductImage", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("BlobName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<Guid>("ProductId")
                        .HasColumnType("uuid");

                    b.Property<string>("Url")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("ProductId");

                    b.ToTable("ProductImages");

                    b.HasData(
                        new
                        {
                            Id = new Guid("9b380e6e-74bb-4c63-9efd-1f34289fd828"),
                            BlobName = "",
                            ProductId = new Guid("490ee5e9-bf99-42d3-89a3-d3750f85d3f4"),
                            Url = "https://via.placeholder.com/300"
                        },
                        new
                        {
                            Id = new Guid("282a2e9c-1e46-4ebb-8ed3-a10513b1e77d"),
                            BlobName = "",
                            ProductId = new Guid("8760cf83-9d00-4d1c-bbe6-372b40842b26"),
                            Url = "https://via.placeholder.com/300"
                        },
                        new
                        {
                            Id = new Guid("967eb9d0-0e6a-4541-aaa4-e1cf1ea28fef"),
                            BlobName = "",
                            ProductId = new Guid("8eff2554-c5a7-448e-a4bc-d72ef21c5f4d"),
                            Url = "https://via.placeholder.com/300"
                        });
                });

            modelBuilder.Entity("haworks.Db.ProductReview", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Comment")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<Guid>("ProductId")
                        .HasColumnType("uuid");

                    b.Property<double>("Rating")
                        .HasColumnType("double precision");

                    b.Property<string>("User")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("ProductId");

                    b.ToTable("ProductReviews");

                    b.HasData(
                        new
                        {
                            Id = new Guid("cde02c48-1ca7-4c68-94d0-180fff73ab98"),
                            Comment = "Great product!",
                            ProductId = new Guid("490ee5e9-bf99-42d3-89a3-d3750f85d3f4"),
                            Rating = 5.0,
                            User = "Alice"
                        },
                        new
                        {
                            Id = new Guid("95c273ee-f3b5-4f7d-ac59-56bb84469466"),
                            Comment = "Good value for money.",
                            ProductId = new Guid("490ee5e9-bf99-42d3-89a3-d3750f85d3f4"),
                            Rating = 4.0,
                            User = "Bob"
                        },
                        new
                        {
                            Id = new Guid("5fb32d39-9761-408b-8ce7-788844fed99a"),
                            Comment = "Excellent quality!",
                            ProductId = new Guid("8760cf83-9d00-4d1c-bbe6-372b40842b26"),
                            Rating = 5.0,
                            User = "Charlie"
                        },
                        new
                        {
                            Id = new Guid("3df7ad1b-60cb-4a4e-8318-333b2dc192b7"),
                            Comment = "Satisfactory.",
                            ProductId = new Guid("8760cf83-9d00-4d1c-bbe6-372b40842b26"),
                            Rating = 3.0,
                            User = "Dana"
                        },
                        new
                        {
                            Id = new Guid("38f79d4f-064c-4c21-9109-d80e4e1b8ed4"),
                            Comment = "Could be better.",
                            ProductId = new Guid("8eff2554-c5a7-448e-a4bc-d72ef21c5f4d"),
                            Rating = 3.0,
                            User = "Eve"
                        },
                        new
                        {
                            Id = new Guid("92158c84-801e-45a1-821b-8a505deb726f"),
                            Comment = "Not worth the price.",
                            ProductId = new Guid("8eff2554-c5a7-448e-a4bc-d72ef21c5f4d"),
                            Rating = 2.0,
                            User = "Frank"
                        });
                });

            modelBuilder.Entity("haworks.Db.Ritual", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<DateTime>("Created")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("CreatorId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("FullTextContent")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<bool>("IsExternalMediaUrl")
                        .HasColumnType("boolean");

                    b.Property<bool>("IsLocked")
                        .HasColumnType("boolean");

                    b.Property<bool>("IsProduct")
                        .HasColumnType("boolean");

                    b.Property<string>("MediaUrl")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Preview")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<double>("Rating")
                        .HasColumnType("double precision");

                    b.Property<string>("RitualType")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<decimal>("TokenAmount")
                        .HasColumnType("numeric");

                    b.Property<DateTime?>("Updated")
                        .HasColumnType("timestamp with time zone");

                    b.HasKey("Id");

                    b.HasIndex("CreatorId");

                    b.ToTable("Rituals");
                });

            modelBuilder.Entity("haworks.Db.User", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text");

                    b.Property<int>("AccessFailedCount")
                        .HasColumnType("integer");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken()
                        .HasColumnType("text");

                    b.Property<string>("Email")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<bool>("EmailConfirmed")
                        .HasColumnType("boolean");

                    b.Property<bool>("LockoutEnabled")
                        .HasColumnType("boolean");

                    b.Property<DateTimeOffset?>("LockoutEnd")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("NormalizedEmail")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("NormalizedUserName")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("PasswordHash")
                        .HasColumnType("text");

                    b.Property<string>("PhoneNumber")
                        .HasColumnType("text");

                    b.Property<bool>("PhoneNumberConfirmed")
                        .HasColumnType("boolean");

                    b.Property<string>("SecurityStamp")
                        .HasColumnType("text");

                    b.Property<bool>("TwoFactorEnabled")
                        .HasColumnType("boolean");

                    b.Property<string>("UserName")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.HasKey("Id");

                    b.HasIndex("NormalizedEmail")
                        .HasDatabaseName("EmailIndex");

                    b.HasIndex("NormalizedUserName")
                        .IsUnique()
                        .HasDatabaseName("UserNameIndex");

                    b.ToTable("AspNetUsers", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<string>", b =>
                {
                    b.HasOne("Microsoft.AspNetCore.Identity.IdentityRole", null)
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<string>", b =>
                {
                    b.HasOne("haworks.Db.User", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<string>", b =>
                {
                    b.HasOne("haworks.Db.User", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<string>", b =>
                {
                    b.HasOne("Microsoft.AspNetCore.Identity.IdentityRole", null)
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("haworks.Db.User", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<string>", b =>
                {
                    b.HasOne("haworks.Db.User", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("haworks.Db.Comment", b =>
                {
                    b.HasOne("haworks.Db.Post", "Post")
                        .WithMany("Comments")
                        .HasForeignKey("PostId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("haworks.Db.User", null)
                        .WithMany("Comments")
                        .HasForeignKey("UserId");

                    b.Navigation("Post");
                });

            modelBuilder.Entity("haworks.Db.OrderItem", b =>
                {
                    b.HasOne("haworks.Db.Order", "Order")
                        .WithMany("OrderItems")
                        .HasForeignKey("OrderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("haworks.Db.Product", "Product")
                        .WithMany()
                        .HasForeignKey("ProductId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Order");

                    b.Navigation("Product");
                });

            modelBuilder.Entity("haworks.Db.Post", b =>
                {
                    b.HasOne("haworks.Db.User", null)
                        .WithMany("Posts")
                        .HasForeignKey("UserId");
                });

            modelBuilder.Entity("haworks.Db.Product", b =>
                {
                    b.HasOne("haworks.Db.Category", "Category")
                        .WithMany("Products")
                        .HasForeignKey("CategoryId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Category");
                });

            modelBuilder.Entity("haworks.Db.ProductAsset", b =>
                {
                    b.HasOne("haworks.Db.Product", "Product")
                        .WithMany("ProductAssets")
                        .HasForeignKey("ProductId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Product");
                });

            modelBuilder.Entity("haworks.Db.ProductImage", b =>
                {
                    b.HasOne("haworks.Db.Product", "Product")
                        .WithMany("ProductImages")
                        .HasForeignKey("ProductId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Product");
                });

            modelBuilder.Entity("haworks.Db.ProductReview", b =>
                {
                    b.HasOne("haworks.Db.Product", "Product")
                        .WithMany("ProductReviews")
                        .HasForeignKey("ProductId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Product");
                });

            modelBuilder.Entity("haworks.Db.Ritual", b =>
                {
                    b.HasOne("haworks.Db.User", "Creator")
                        .WithMany("Rituals")
                        .HasForeignKey("CreatorId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Creator");
                });

            modelBuilder.Entity("haworks.Db.Category", b =>
                {
                    b.Navigation("Products");
                });

            modelBuilder.Entity("haworks.Db.Order", b =>
                {
                    b.Navigation("OrderItems");
                });

            modelBuilder.Entity("haworks.Db.Post", b =>
                {
                    b.Navigation("Comments");
                });

            modelBuilder.Entity("haworks.Db.Product", b =>
                {
                    b.Navigation("ProductAssets");

                    b.Navigation("ProductImages");

                    b.Navigation("ProductReviews");
                });

            modelBuilder.Entity("haworks.Db.User", b =>
                {
                    b.Navigation("Comments");

                    b.Navigation("Posts");

                    b.Navigation("Rituals");
                });
#pragma warning restore 612, 618
        }
    }
}
