using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace RitualWorks.Tests
{
    public class DockerHelper
    {
        private const string ImageName = "postgres:13";
        private const string ContainerName = "postgres_test_container";
        private const int PostgresStartTimeout = 60000; // Increased timeout for PostgreSQL to be ready

        // Credentials and database name
        private const string PostgresPassword = "my-secret-pw";
        private const string TestDbName = "test_db";
        private const string TestUser = "test_user";
        private const string TestPassword = "test_password";
        private const string DefaultUsername = "postgres";

        private readonly ILogger<DockerHelper> _logger;

        public DockerHelper(ILogger<DockerHelper> logger)
        {
            _logger = logger;
        }

        public async Task StartContainer(int port = 0)
        {
            if (port == 0)
            {
                port = GetAvailablePort();
            }

            string dockerUri = GetDockerUri();
            using var client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();

            _logger.LogInformation("Starting PostgreSQL container...");

            // Parallel tasks to check and remove existing containers and pull image if necessary
            var tasks = new List<Task>
            {
                Task.Run(() => RemoveExistingContainersAsync(client)),
                Task.Run(() => EnsureImageIsPulledAsync(client))
            };

            await Task.WhenAll(tasks);

            var config = new Config
            {
                Env = new List<string>
                {
                    $"POSTGRES_PASSWORD={PostgresPassword}",
                    $"POSTGRES_DB={TestDbName}"
                }
            };

            var hostConfig = new HostConfig
            {
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    { "5432/tcp", new List<PortBinding> { new PortBinding { HostPort = port.ToString() } } }
                }
            };

            var createContainerResponse = await client.Containers.CreateContainerAsync(new CreateContainerParameters(config)
            {
                Image = ImageName,
                Name = ContainerName,
                HostConfig = hostConfig
            });

            await client.Containers.StartContainerAsync(createContainerResponse.ID, new ContainerStartParameters());

            // Wait for PostgreSQL to initialize
            await WaitForPostgresToBeReadyAsync(port);

            // Setup the user and grant necessary permissions
            await SetupDatabaseUserAsync(port);
        }

        public async Task StopContainer()
        {
            string dockerUri = GetDockerUri();
            using var client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
            await RemoveExistingContainersAsync(client);
        }

        private async Task RemoveExistingContainersAsync(DockerClient client)
        {
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    { "name", new Dictionary<string, bool> { { ContainerName, true } } }
                }
            });

            var tasks = containers.Select(container =>
                client.Containers.StopContainerAsync(container.ID, new ContainerStopParameters())
                    .ContinueWith(_ => client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true }))).ToArray();

            await Task.WhenAll(tasks);
        }

        private async Task EnsureImageIsPulledAsync(DockerClient client)
        {
            var images = await client.Images.ListImagesAsync(new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    { "reference", new Dictionary<string, bool> { { ImageName, true } } }
                }
            });

            if (!images.Any())
            {
                _logger.LogInformation("Pulling image {ImageName}", ImageName);
                await client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = ImageName }, null, new Progress<JSONMessage>());
            }
        }

        private async Task WaitForPostgresToBeReadyAsync(int port)
        {
            var timeout = Task.Delay(PostgresStartTimeout);
            var connectionString = $"Host=localhost;Port={port};Username={DefaultUsername};Password={PostgresPassword};";

            while (!timeout.IsCompleted)
            {
                try
                {
                    using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync();
                    _logger.LogInformation("PostgreSQL container is ready.");
                    return; // Connected successfully
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Waiting for PostgreSQL to be ready: {ex.Message}");
                    await Task.Delay(1000); // Wait 1 second before retrying
                }
            }

            throw new Exception("PostgreSQL did not start within the allocated timeout.");
        }

        private async Task SetupDatabaseUserAsync(int port)
        {
            var connectionString = $"Host=localhost;Port={port};Username={DefaultUsername};Password={PostgresPassword};";
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var createUserQuery = $@"
                CREATE USER {TestUser} WITH PASSWORD '{TestPassword}';
                GRANT ALL PRIVILEGES ON DATABASE {TestDbName} TO {TestUser};
                ALTER USER {TestUser} CREATEDB;";

            using (var cmd = new NpgsqlCommand(createUserQuery, connection))
            {
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private int GetAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private string GetDockerUri()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "npipe://./pipe/docker_engine";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "unix:///var/run/docker.sock";
            }
            else
            {
                throw new NotSupportedException("Unsupported OS platform");
            }
        }
    }
}
