using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace RitualWorks.Services
{
    public class VaultSettings
    {
        public string VaultAddress { get; set; }
    }

    public class VaultService
    {
        private readonly HttpClient _httpClient;
        private readonly string _vaultToken;
        private readonly ILogger<VaultService> _logger;

        public VaultService(HttpClient httpClient, IOptions<VaultSettings> options, ILogger<VaultService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            // Set the Vault base address from configuration
            _httpClient.BaseAddress = new Uri(options.Value.VaultAddress);

            // Retrieve the Vault token securely from environment variables
            _vaultToken = Environment.GetEnvironmentVariable("VAULT_ROOT_TOKEN");

            if (string.IsNullOrEmpty(_vaultToken))
            {
                _logger.LogError("Vault token not found. Ensure VAULT_ROOT_TOKEN is set in the environment.");
                throw new InvalidOperationException("Vault token not found. Ensure VAULT_ROOT_TOKEN is set in the environment.");
            }

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _vaultToken);
        }

        public async Task<JObject> GetSecretAsync(string secretPath)
        {
            try
            {
                _logger.LogInformation("Fetching secret from path: {SecretPath}", secretPath);
                var response = await _httpClient.GetAsync($"/v1/{secretPath}");

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Secret fetched successfully from path: {SecretPath}", secretPath);
                    return JObject.Parse(jsonResponse);
                }

                _logger.LogError("Failed to fetch secret from path: {SecretPath}. Status code: {StatusCode}, Reason: {Reason}", 
                    secretPath, response.StatusCode, response.ReasonPhrase);

                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error details: {ErrorContent}", errorContent);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accessing Vault at path: {SecretPath}", secretPath);
                return null;
            }
        }
    }
}
