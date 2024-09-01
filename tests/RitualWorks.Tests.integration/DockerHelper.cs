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

namespace RitualWorks.Tests
{
    public class DockerContainerConfig
    {
        public string ImageName { get; set; }
        public string ContainerName { get; set; }
        public IDictionary<string, string> EnvironmentVariables { get; set; }
        public IDictionary<string, IList<PortBinding>> PortBindings { get; set; }
        public int StartTimeout { get; set; } = 30000; // Default to 30 seconds
        public Func<int, Task<bool>> HealthCheck { get; set; } // Delegate for health check logic
    }

    public class DockerHelper
    {
        private readonly DockerContainerConfig _config;
        private readonly ILogger<DockerHelper> _logger;
        private int _hostPort;

        public DockerHelper(DockerContainerConfig config, ILogger<DockerHelper> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task StartContainer()
        {
            string dockerUri = GetDockerUri();
            using var client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();

            _logger.LogInformation("Starting container {ContainerName}", _config.ContainerName);

            var tasks = new List<Task>
            {
                Task.Run(() => RemoveExistingContainersAsync(client)),
                Task.Run(() => EnsureImageIsPulledAsync(client))
            };

            await Task.WhenAll(tasks);

            var config = new Config
            {
                Env = _config.EnvironmentVariables.Select(kv => $"{kv.Key}={kv.Value}").ToList()
            };

            var hostConfig = new HostConfig();

            // Check if PortBindings are provided in DockerContainerConfig
            if (_config.PortBindings != null && _config.PortBindings.Any())
            {
                hostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>();

                foreach (var portBinding in _config.PortBindings)
                {
                    var bindings = portBinding.Value.Select(binding =>
                    {
                        if (string.IsNullOrEmpty(binding.HostPort) || binding.HostPort == "0")
                        {
                            binding.HostPort = GetAvailablePort().ToString();
                        }
                        return binding;
                    }).ToList();

                    hostConfig.PortBindings[portBinding.Key] = bindings;
                }

                _hostPort = int.Parse(hostConfig.PortBindings.First().Value.First().HostPort);
            }
            else
            {
                // Use GetAvailablePort as fallback when PortBindings are not specified
                _hostPort = GetAvailablePort();
                hostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    { "5432/tcp", new List<PortBinding> { new PortBinding { HostPort = _hostPort.ToString() } } }
                };
            }

            _logger.LogInformation("Creating container {ContainerName}", _config.ContainerName);

            var createContainerResponse = await client.Containers.CreateContainerAsync(new CreateContainerParameters(config)
            {
                Image = _config.ImageName,
                Name = _config.ContainerName,
                HostConfig = hostConfig
            });

            _logger.LogInformation("Starting container {ContainerName}", _config.ContainerName);

            var started = await client.Containers.StartContainerAsync(createContainerResponse.ID, new ContainerStartParameters());

            if (!started)
            {
                throw new Exception($"Failed to start container {_config.ContainerName}");
            }

            _logger.LogInformation("Container {ContainerName} started", _config.ContainerName);

            await WaitForContainerToBeReadyAsync();
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
                    { "name", new Dictionary<string, bool> { { _config.ContainerName, true } } }
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
                    { "reference", new Dictionary<string, bool> { { _config.ImageName, true } } }
                }
            });

            if (!images.Any())
            {
                _logger.LogInformation("Pulling image {ImageName}", _config.ImageName);
                await client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = _config.ImageName }, null, new Progress<JSONMessage>());
            }
        }

        private async Task WaitForContainerToBeReadyAsync()
        {
            var timeout = Task.Delay(_config.StartTimeout);

            _logger.LogInformation("Waiting for container {ContainerName} to be ready", _config.ContainerName);

            while (!timeout.IsCompleted)
            {
                try
                {
                    if (await _config.HealthCheck(_hostPort))
                    {
                        _logger.LogInformation("Container {ContainerName} is ready", _config.ContainerName);
                        return;
                    }
                    else
                    {
                        _logger.LogWarning("Health check failed, retrying...");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Health check exception: {Message}", ex.Message);
                }

                await Task.Delay(1000);
            }

            throw new Exception($"Container {_config.ContainerName} did not start within the allocated timeout.");
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
