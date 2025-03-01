/*using System;
using System.Data;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using Testcontainers.PostgreSql;
using Testcontainers.Vault;
using Xunit;
using Xunit.Abstractions;
using Npgsql;
using haworks.Services;
using haworks.Db;

namespace haworks.Tests.Integration
{
    public class CredentialRotationTests : IAsyncLifetime
    {
        private readonly VaultContainer _vaultContainer;
        private readonly PostgreSqlContainer _postgresContainer;
        private readonly string _vaultRoleId;
        private readonly string _vaultSecretId;
        private ServiceProvider _serviceProvider;
        private readonly ITestOutputHelper _output;

        public CredentialRotationTests(ITestOutputHelper output)
        {
            _output = output;
            
            _vaultContainer = new VaultBuilder()
                .WithImage("vault:1.15")
                .WithPort(8200)
                .WithCommand("server -dev -dev-root-token-id=root")
                .Build();

            _postgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:15")
                .WithPassword("initial_password")
                .Build();
        }

        public async Task InitializeAsync()
        {
            await _vaultContainer.StartAsync();
            await _postgresContainer.StartAsync();

            // Initialize Vault with database secrets engine
            await InitializeVaultDatabaseSecretEngine();
            
            // Configure DI container
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Vault:Address"] = _vaultContainer.GetConnectionString(),
                    ["Vault:RoleIdPath"] = "/tmp/role_id",
                    ["Vault:SecretIdPath"] = "/tmp/secret_id",
                    ["Database:ConnectionString"] = 
                        $"Host={_postgresContainer.Hostname};Port={_postgresContainer.GetMappedPublicPort(5432)}"
                })
                .Build());
            
            services.AddVaultServices();
            services.AddDbContext<haworksContext>();
            
            _serviceProvider = services.BuildServiceProvider();

            // Write Vault app role credentials
            await File.WriteAllTextAsync("/tmp/role_id", _vaultRoleId);
            await File.WriteAllTextAsync("/tmp/secret_id", _vaultSecretId);
        }

        private async Task InitializeVaultDatabaseSecretEngine()
        {
            // Vault initialization steps to configure database secrets engine
            // This would typically execute vault CLI commands via exec in container
            // Pseudocode for demonstration:
            await _vaultContainer.ExecAsync(new[]
            {
                "vault", "secrets", "enable", "database"
            });
            
            await _vaultContainer.ExecAsync(new[]
            {
                "vault", "write", "database/config/postgres",
                $"plugin_name=postgresql-database-plugin",
                $"allowed_roles=app",
                $"connection_url=postgresql://postgres:initial_password@postgres:5432/postgres"
            });

            await _vaultContainer.ExecAsync(new[]
            {
                "vault", "write", "database/roles/app",
                "db_name=postgres",
                "creation_statements=CREATE ROLE \"{{name}}\" WITH LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}';",
                "default_ttl=1m",
                "max_ttl=2m"
            });

            // Create app role and get credentials
            var roleId = await _vaultContainer.ExecReadOutputAsync(
                "vault", "read", "-field=role_id", "auth/approle/role/app/role-id");
            var secretId = await _vaultContainer.ExecReadOutputAsync(
                "vault", "write", "-f", "-field=secret_id", "auth/approle/role/app/secret-id");

            _vaultRoleId = roleId.Trim();
            _vaultSecretId = secretId.Trim();
        }

        [Fact]
        public async Task CredentialRotation_WithActiveConnections_MaintainsAvailability()
        {
            // Arrange
            var dbContext = _serviceProvider.GetRequiredService<haworksContext>();
            var vaultService = _serviceProvider.GetRequiredService<IVaultService>();
            
            // Start long-running operation
            var longRunningTask = Task.Run(async () => 
            {
                while(true)
                {
                    await dbContext.Database.OpenConnectionAsync();
                    await Task.Delay(100);
                }
            });

            // Act - Force credential rotation
            await Task.Delay(TimeSpan.FromSeconds(70)); // Wait for TTL expiration
            var newCredentials = await vaultService.GetDatabaseCredentialsAsync();
            
            // Assert
            await AssertConnectionWorksWithCredentials(newCredentials);
            await AssertDatabaseOperationsContinue(dbContext);

            // Cleanup
            longRunningTask.Dispose();
        }

        private async Task AssertConnectionWorksWithCredentials(
            (string Username, SecureString Password) credentials)
        {
            using var conn = new NpgsqlConnection(
                $"Host={_postgresContainer.Hostname};"
                + $"Username={credentials.Username};"
                + $"Password={new NetworkCredential("", credentials.Password).Password}");
            
            await conn.OpenAsync();
            Assert.Equal(ConnectionState.Open, conn.State);
        }

        private async Task AssertDatabaseOperationsContinue(haworksContext context)
        {
            // Verify existing migration
            await context.Database.MigrateAsync();

            // Perform test query
            var result = await context.Database.ExecuteSqlRawAsync("SELECT 1");
            Assert.Equal(1, result);
        }

        public async Task DisposeAsync()
        {
            await _vaultContainer.DisposeAsync();
            await _postgresContainer.DisposeAsync();
            _serviceProvider.Dispose();
        }
    }
}*/