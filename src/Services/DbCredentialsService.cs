using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace RitualWorks.Services
{
    public class DbCredentialsService : BackgroundService
    {
        private readonly string _credentialsFilePath;
        private readonly ILogger<DbCredentialsService> _logger;
        private readonly IConfiguration _configuration;
        private FileSystemWatcher _fileWatcher;

        public string Username { get; private set; }
        public string Password { get; private set; }

        public DbCredentialsService(ILogger<DbCredentialsService> logger, IConfiguration configuration)
        {
            _credentialsFilePath = configuration.GetValue<string>("Vault:CredentialsFilePath") ?? "/vault/secrets/db-creds.json"; // Path to the db-creds.json
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting the DbCredentialsService...");

            // Read the credentials initially
            await ReadCredentialsAsync();

            // Set up a file system watcher to monitor changes to the credentials file
            _fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(_credentialsFilePath))
            {
                Filter = Path.GetFileName(_credentialsFilePath),
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += async (sender, args) =>
            {
                _logger.LogInformation("Credentials file changed. Reloading...");
                await ReadCredentialsAsync();
            };

            await Task.CompletedTask;
        }

        public async Task ReadCredentialsAsync()
        {
            try
            {
                _logger.LogInformation("Reading database credentials from file: {filePath}", _credentialsFilePath);

                if (!File.Exists(_credentialsFilePath))
                {
                    _logger.LogError("Credentials file not found at path: {filePath}", _credentialsFilePath);
                    return;
                }

                // Read and deserialize the credentials
                var fileContent = await File.ReadAllTextAsync(_credentialsFilePath);
                var credentials = JsonConvert.DeserializeObject<DbCredentials>(fileContent);

                Username = credentials.Username;
                Password = credentials.Password;

                _logger.LogInformation("Database credentials updated successfully: Username: {username}", Username);

                // Update the connection string in configuration
                var connectionString = $"Host=postgres_primary;Port=5432;Database=your_postgres_db;Username={Username};Password={Password}";
                _configuration["ConnectionStrings:DefaultConnection"] = connectionString;
                _logger.LogInformation("Connection string updated in configuration");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while reading the database credentials");
            }
        }
    }

    public class DbCredentials
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
