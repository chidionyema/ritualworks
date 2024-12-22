﻿using haworks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Newtonsoft.Json;
using System.IO;
using System;
namespace haworks.Db
{
    public class haworksContextFactory : IDesignTimeDbContextFactory<haworksContext>
    {
        private readonly IConnectionStringProvider _connectionStringProvider;

        // Constructor for runtime DI
        public haworksContextFactory(IConnectionStringProvider connectionStringProvider)
        {
            _connectionStringProvider = connectionStringProvider;
        }

        // Fallback constructor for design-time operations
        public haworksContextFactory() { }

        public haworksContext CreateDbContext(string[] args)
        {
            string connectionString;
            connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
                    ?? throw new InvalidOperationException("Connection string not found in environment variables.");
                Console.WriteLine($"[Debug] Connection string from environment variable: {connectionString}");

            // Create DbContextOptions
            var optionsBuilder = new DbContextOptionsBuilder<haworksContext>();
            optionsBuilder.UseNpgsql(connectionString);

            // Create and return the context
            return new haworksContext(optionsBuilder.Options);
        }
    }
}
