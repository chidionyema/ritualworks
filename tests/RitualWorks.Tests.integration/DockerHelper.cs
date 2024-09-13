using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace RitualWorks.Tests
{
    public class DockerHelper
    {
        private readonly ILogger<DockerHelper> _logger;
        private readonly string _imageName;
        private readonly string _containerName;
        private readonly int _serviceStartTimeout;
        private readonly Func<int, Task> _postStartCallback;
        private DockerClient _dockerClient;
        private string _containerId;

        public DockerHelper(ILogger<DockerHelper> logger, string imageName, string containerName, int serviceStartTimeout, Func<int, Task> postStartCallback = null)
        {
            _logger = logger;
            _imageName = imageName;
            _containerName = containerName;
            _serviceStartTimeout = serviceStartTimeout;
            _postStartCallback = postStartCallback;
            _dockerClient = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        }

        public async Task StartContainer(int hostPort, int containerPort, List<string> environmentVariables, List<string> command = null)
        {
            try
            {
                _logger.LogInformation($"Starting container {_containerName}...");

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
                    // Create and start a new container
                    var createParams = new CreateContainerParameters
                    {
                        Image = _imageName,
                        Name = _containerName,
                        Env = environmentVariables,
                        HostConfig = new HostConfig
                        {
                            PortBindings = new Dictionary<string, IList<PortBinding>>
                            {
                                [$"{containerPort}/tcp"] = new List<PortBinding> { new PortBinding { HostPort = hostPort.ToString() } }
                            },
                        },
                        Cmd = command
                    };

                    var response = await _dockerClient.Containers.CreateContainerAsync(createParams);
                    _containerId = response.ID;

                    _logger.LogInformation($"Created container {_containerName} with ID: {_containerId}");

                    await _dockerClient.Containers.StartContainerAsync(_containerId, null);
                    _logger.LogInformation("Container started successfully.");
                }

                // Run post-start callback if specified
                if (_postStartCallback != null)
                {
                    _logger.LogInformation("Running post-start callback...");
                    await _postStartCallback(hostPort);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error starting container {_containerName}: {ex.Message}");
                throw;
            }
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
