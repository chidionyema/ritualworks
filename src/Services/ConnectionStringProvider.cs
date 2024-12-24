
using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;  
using haworks.Contracts;
using haworks.Models;  
namespace haworks.Services  
{
    public class ConnectionStringProvider : IConnectionStringProvider
    {
        private readonly IConfiguration _configuration;
        private readonly object _lock = new object();
        private string _connectionString;
        private int _leaseDuration;
        private readonly string _dbCredsFilePath = "/vault/secrets/db-creds.json";

        public ConnectionStringProvider(IConfiguration configuration)
        {
            _configuration = configuration;
            UpdateConnectionString();
        }

        public string GetConnectionString()
        {
            lock (_lock)
            {
                return _connectionString;
            }
        }

        public int GetLeaseDuration()
        {
            lock (_lock)
            {
                return _leaseDuration;
            }
        }

        public void UpdateConnectionString()
        {
            var dbCredentials = LoadDbCredentialsFromFile();

            var existingConnectionString = _configuration.GetConnectionString("DefaultConnection");

            var updatedConnectionString = new NpgsqlConnectionStringBuilder(existingConnectionString)
            {
                Username = dbCredentials.Username,
                Password = dbCredentials.Password
            }.ConnectionString;

            lock (_lock)
            {
                _connectionString = updatedConnectionString;
                // Clear the connection pool to close existing connections with old credentials
                NpgsqlConnection.ClearAllPools();
            }
        }

        private DatabaseCredentials LoadDbCredentialsFromFile()
        {
            var credentialsFilePath = _dbCredsFilePath;

            if (!File.Exists(credentialsFilePath))
            {
                throw new FileNotFoundException($"Vault credentials file not found at: {credentialsFilePath}");
            }

            var jsonContent = File.ReadAllText(credentialsFilePath);
    
            using var jsonDocument = JsonDocument.Parse(jsonContent);
            var rootElement = jsonDocument.RootElement;

            // Read the lease duration from the root element
            if (!rootElement.TryGetProperty("lease_duration", out var leaseDurationElement))
            {
                throw new InvalidOperationException("Lease duration not found in credentials file.");
            }
            var leaseDuration = leaseDurationElement.GetInt32();

            // Read the data object containing the credentials
            if (!rootElement.TryGetProperty("data", out var dataElement))
            {
                throw new InvalidOperationException("Credentials data not found in credentials file.");
            }

            var username = dataElement.GetProperty("username").GetString();
            var password = dataElement.GetProperty("password").GetString();

            // Store the lease duration in a thread-safe manner
            lock (_lock)
            {
                _leaseDuration = leaseDuration;
            }

            // Log to verify (ensure sensitive data is not logged in production)
            Console.WriteLine($"Loaded DB credentials: Username={username}, Lease Duration={_leaseDuration} seconds");

            return new DatabaseCredentials
            {
                Username = username,
                Password = password
            };
        }
    }
}
