using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Npgsql;
using haworks.Contracts;
using haworks.Models;

namespace haworks.Services
{
    public class ConnectionStringProvider : IConnectionStringProvider
    {
        private readonly IConfiguration _configuration;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1); // Async-friendly lock to manage access
        private string _connectionString;
        private int _leaseDuration;
        private readonly string _dbCredsFilePath = "/vault/secrets/db-creds.json";

        public ConnectionStringProvider(IConfiguration configuration)
        {
            // Dependency injection ensures we have a configuration object for reading app settings.
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // Trigger an initial update to set up the connection string.
            _ = UpdateConnectionStringAsync(); // Fire-and-forget pattern avoids blocking the constructor.
        }

        /// <summary>
        /// Gets the current connection string in a thread-safe manner.
        /// </summary>
        public async Task<string> GetConnectionStringAsync()
        {
            await _semaphore.WaitAsync(); // Ensures thread-safe access without blocking other async tasks.
            try
            {
                return _connectionString;
            }
            finally
            {
                _semaphore.Release(); // Always release the semaphore to prevent deadlocks.
            }
        }

        /// <summary>
        /// Gets the current lease duration for the database credentials.
        /// </summary>
        public async Task<int> GetLeaseDurationAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                return _leaseDuration;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Updates the connection string using credentials from Vault.
        /// </summary>
        public async Task UpdateConnectionStringAsync()
        {
            try
            {
                // Load the latest credentials from the Vault file.
                var dbCredentials = await LoadDbCredentialsFromFileAsync();

                // Get the base connection string from the app settings.
                var existingConnectionString = _configuration.GetConnectionString("DefaultConnection");

                // Update the connection string with the new credentials.
                var updatedConnectionString = new NpgsqlConnectionStringBuilder(existingConnectionString)
                {
                    Username = dbCredentials.Username,
                    Password = dbCredentials.Password
                }.ConnectionString;

                // Thread-safe update of the connection string and lease duration.
                await _semaphore.WaitAsync();
                try
                {
                    _connectionString = updatedConnectionString;
                    _leaseDuration = dbCredentials.LeaseDuration;

                    // Clear the connection pool to ensure no stale connections are reused.
                    NpgsqlConnection.ClearAllPools();
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating connection string: {ex.Message}");
                throw; // Rethrow to signal failure to the caller.
            }
        }

        /// <summary>
        /// Reads the database credentials from the Vault JSON file.
        /// </summary>
        private async Task<DatabaseCredentials> LoadDbCredentialsFromFileAsync()
        {
            // Verify the file exists to avoid unexpected errors during file read operations.
            if (!File.Exists(_dbCredsFilePath))
            {
                throw new FileNotFoundException($"Vault credentials file not found at: {_dbCredsFilePath}");
            }

            // Open the file for async reading.
            using var fileStream = new FileStream(_dbCredsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Parse the JSON file content asynchronously for better performance under load.
            using var jsonDocument = await JsonDocument.ParseAsync(fileStream);
            var rootElement = jsonDocument.RootElement;

            // Validate and extract the lease duration.
            if (!rootElement.TryGetProperty("lease_duration", out var leaseDurationElement))
            {
                throw new InvalidOperationException("Lease duration not found in credentials file.");
            }
            var leaseDuration = leaseDurationElement.GetInt32();

            // Validate and extract the data object containing the username and password.
            if (!rootElement.TryGetProperty("data", out var dataElement))
            {
                throw new InvalidOperationException("Credentials data not found in credentials file.");
            }

            var username = dataElement.GetProperty("username").GetString();
            var password = dataElement.GetProperty("password").GetString();

            // Store the lease duration in a thread-safe manner.
            await _semaphore.WaitAsync();
            try
            {
                _leaseDuration = leaseDuration;
            }
            finally
            {
                _semaphore.Release();
            }

            // Log to verify (ensure sensitive data is not logged in production).
            Console.WriteLine($"Loaded DB credentials: Username={username}, Lease Duration={leaseDuration} seconds");

            return new DatabaseCredentials
            {
                Username = username,
                Password = password,
                LeaseDuration = leaseDuration
            };
        }
    }

    /// <summary>
    /// Represents the structure of database credentials loaded from Vault.
    /// </summary>
    public class DatabaseCredentials
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public int LeaseDuration { get; set; }
    }
}
