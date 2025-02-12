using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Haworks.Tests
{
    public class DockerHelper
    {
        private readonly ILogger<DockerHelper> _logger;
        private readonly string _imageName;
        private readonly string _containerName;
        private readonly Func<int, Task> _postStartCallback;
        private DockerClient _dockerClient;
        private string _containerId;
        private readonly TimeSpan _healthCheckTimeout; // Configurable timeout

        public string ContainerName => _containerName;

        public DockerHelper(ILogger<DockerHelper> logger, string imageName, string containerName, Func<int, Task> postStartCallback = null, TimeSpan? healthCheckTimeout = null)
        {
            _logger = logger;
            _imageName = imageName;
            _containerName = containerName;
            _postStartCallback = postStartCallback;
            _dockerClient = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
            _healthCheckTimeout = healthCheckTimeout ?? TimeSpan.FromMinutes(5);
        }

        public DockerClient GetDockerClient()
        {
            return _dockerClient;
        }

        public async Task StartContainer(int hostPort, int containerPort, List<string> environmentVariables, List<string> command = null)
        {
            try
            {
                _logger.LogInformation($"Starting container {_containerName} with image {_imageName} on host port {hostPort} and container port {containerPort}...");

                // Check if the container already exists
                var containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters() { All = true });
                var existingContainer = containers.FirstOrDefault(c => c.Names.Contains($"/{_containerName}"));

                if (existingContainer != null)
                {
                    _containerId = existingContainer.ID;
                    _logger.LogInformation($"Container {_containerName} already exists with ID: {_containerId}");

                    if (existingContainer.State != "running")
                    {
                        _logger.LogInformation("Starting existing container...");
                        await _dockerClient.Containers.StartContainerAsync(_containerId, null);
                        _logger.LogInformation("Container started successfully.");
                    }
                    else
                    {
                        _logger.LogInformation("Container is already running.");
                    }
                }
                else
                {
                    // Retrieve the dbUser from the environment variables
                    var dbUser = environmentVariables.FirstOrDefault(e => e.StartsWith("POSTGRES_USER="))?
                        .Split('=')[1] ?? "myuser";

                    // Create container parameters with a health check.
                    var createParams = new CreateContainerParameters
                    {
                        Image = _imageName,
                        Name = _containerName,
                        Env = environmentVariables,
                        HostConfig = new HostConfig
                        {
                            PortBindings = new Dictionary<string, IList<PortBinding>>
                            {
                                // Map container port (5432) to hostPort.
                                [$"{containerPort}/tcp"] = new List<PortBinding> { new PortBinding { HostPort = hostPort.ToString() } }
                            },
                        },
                        // Configure the Docker health check for PostgreSQL.
                        Healthcheck = new HealthConfig
                        {
                            Test = new List<string> { "CMD-SHELL", $"pg_isready -U {dbUser}" },
                            Interval = TimeSpan.FromSeconds(5),    // 5 seconds
                            Timeout = TimeSpan.FromSeconds(1),     // 1 second
                            Retries = 5,
                        },
                        Cmd = command
                    };

                    var response = await _dockerClient.Containers.CreateContainerAsync(createParams);
                    _containerId = response.ID;

                    _logger.LogInformation($"Created container {_containerName} with ID: {_containerId}");

                    await _dockerClient.Containers.StartContainerAsync(_containerId, null);
                    _logger.LogInformation("Container started successfully.");
                }

                if (_postStartCallback != null)
                {
                    _logger.LogInformation($"Executing post-start callback on port {hostPort}...");
                    await _postStartCallback(hostPort);
                }

                // Optionally, if you want to support dynamic host port assignment:
                // if (hostPort == 0) { ... retrieve the assigned host port from inspection ... }

                // Wait for container health using our configurable timeout.
                await WaitForContainerToBeHealthy();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error starting container {_containerName}: {ex.Message}");
                throw new Exception($"Failed to start Docker container '{_containerName}'. See logs for details.", ex);
            }
        }

        public async Task WaitForContainerToBeHealthy()
        {
            _logger.LogInformation("Waiting for the container to become healthy...");

            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < _healthCheckTimeout)
            {
                var containerStatus = await _dockerClient.Containers.InspectContainerAsync(_containerId);
                if (containerStatus.State.Health?.Status == "healthy" || containerStatus.State.Status == "running")
                {
                    _logger.LogInformation("Container is healthy.");
                    return;
                }
                await Task.Delay(1000);
            }
            _logger.LogWarning($"Container {_containerName} did not become healthy within {_healthCheckTimeout}.");
        }

        public async Task StopContainer()
        {
            if (string.IsNullOrEmpty(_containerId))
            {
                _logger.LogWarning("No container is currently running or has been identified.");
                return;
            }
            try
            {
                _logger.LogInformation($"Stopping container {_containerName}...");
                await _dockerClient.Containers.StopContainerAsync(_containerId, new ContainerStopParameters { WaitBeforeKillSeconds = 10 });
                _logger.LogInformation("Container stopped successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error stopping container {_containerName}: {ex.Message}");
            }
        }

        public async Task RemoveContainer()
        {
            if (string.IsNullOrEmpty(_containerId))
            {
                _logger.LogWarning("No container is currently running or has been identified for removal.");
                return;
            }
            try
            {
                _logger.LogInformation($"Removing container {_containerName}...");
                await _dockerClient.Containers.RemoveContainerAsync(_containerId, new ContainerRemoveParameters { Force = true });
                _logger.LogInformation("Container removed successfully.");
                _containerId = null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error removing container {_containerName}: {ex.Message}");
            }
        }
    }
}
