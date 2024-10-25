// ConnectionStringProvider.cs

using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RitualWorks.Models; // Adjust the namespace as needed
using RitualWorks.Contracts;
namespace RitualWorks.Services // Adjust the namespace as needed
{

    public class ConnectionStringProvider : IConnectionStringProvider
    {
        private readonly IConfiguration _configuration;
        private readonly object _lock = new object();
        private string _connectionString;
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

        public void UpdateConnectionString()
        {
            var dbCredentials = LoadDbCredentialsFromFile();

            var existingConnectionString = _configuration.GetConnectionString("DefaultConnection");

            var updatedConnectionString = new Npgsql.NpgsqlConnectionStringBuilder(existingConnectionString)
            {
                Username = dbCredentials.Username,
                Password = dbCredentials.Password
            }.ConnectionString;

            lock (_lock)
            {
                _connectionString = updatedConnectionString;
            }
        }

        private DatabaseCredentials LoadDbCredentialsFromFile()
        {
            var username = _configuration["DB_USERNAME"];
            var password = _configuration["DB_PASSWORD"];

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                return new DatabaseCredentials { Username = username, Password = password };
            }

            var credentialsFilePath = _dbCredsFilePath;

            if (!File.Exists(credentialsFilePath))
            {
                throw new FileNotFoundException($"Vault credentials file not found at: {credentialsFilePath}");
            }

            var jsonContent = File.ReadAllText(credentialsFilePath);
            Console.WriteLine($"Loaded json Content: {jsonContent}");

            var dbCredentials = JsonSerializer.Deserialize<DatabaseCredentials>(jsonContent);

            // Log to verify
            Console.WriteLine($"Loaded DB credentials: {dbCredentials.Username}, {dbCredentials.Password}");

            return dbCredentials;
        }
    }
}
