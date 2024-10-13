using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Newtonsoft.Json;
using RitualWorks.Models;
using Newtonsoft.Json.Linq;

namespace RitualWorks.Services
{
    public class VaultSettings
    {
        public string VaultAddress { get; set; } // URL for the Vault server
        public string TokenPath { get; set; } // Optional: Path to a file or source for the token
    }

    public class VaultService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<VaultService> _logger;
        private readonly string _vaultToken;

        public VaultService(HttpClient httpClient, ILogger<VaultService> logger, string vaultToken)
        {
            _httpClient = httpClient;
            _logger = logger;
            _vaultToken = vaultToken ?? throw new ArgumentNullException(nameof(vaultToken));

            _logger.LogInformation("VaultService initialized with Vault token.");
        }

        public async Task<Dictionary<string, string>> FetchSecretsAsync(string secretPath, params string[] keys)
        {
            try
            {
                _logger.LogInformation("Fetching secrets from Vault at path: {SecretPath}", secretPath);

                var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/{secretPath}");
                request.Headers.Add("X-Vault-Token", _vaultToken);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to fetch secrets from Vault. Status Code: {StatusCode}. Reason: {ReasonPhrase}",
                        response.StatusCode, response.ReasonPhrase);
                    throw new HttpRequestException($"Error fetching secrets from Vault: {response.ReasonPhrase}");
                }

                var content = await response.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(content);

                var secrets = new Dictionary<string, string>();

                foreach (var key in keys)
                {
                    if (json.RootElement.TryGetProperty("data", out var data) &&
                        data.TryGetProperty(key, out var value))
                    {
                        secrets[key] = value.GetString();
                    }
                    else
                    {
                        _logger.LogWarning("Secret key '{Key}' not found in Vault response.", key);
                    }
                }

                return secrets;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching secrets from Vault.");
                throw;
            }
        }
        public async Task<DatabaseCredentials> FetchPostgresCredentialsAsync(string role)
        {
            _httpClient.DefaultRequestHeaders.Remove("X-Vault-Token");
            _httpClient.DefaultRequestHeaders.Add("X-Vault-Token", _vaultToken);

            var response = await _httpClient.GetAsync($"/v1/database/creds/{role}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);

            var username = json["data"]?["username"]?.ToString();
            var password = json["data"]?["password"]?.ToString();
            var ttl = json["lease_duration"]?.ToObject<int>() ?? 0;

            return new DatabaseCredentials
            {
                Username = username,
                Password = password
            };
        }

 }

public class VaultSecretResponse
{
    [JsonProperty("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonProperty("lease_id")]
    public string LeaseId { get; set; } = string.Empty;

    [JsonProperty("renewable")]
    public bool Renewable { get; set; }

    [JsonProperty("lease_duration")]
    public int LeaseDuration { get; set; }

    [JsonProperty("data")]
    public Dictionary<string, string> Data { get; set; }

    [JsonProperty("wrap_info")]
    public object WrapInfo { get; set; }

    [JsonProperty("warnings")]
    public object Warnings { get; set; }

    [JsonProperty("auth")]
    public object Auth { get; set; }
}

}
