using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
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
        _httpClient.BaseAddress = new Uri(options.Value.VaultAddress);

        _vaultToken = Environment.GetEnvironmentVariable("VAULT_ROOT_TOKEN");

        if (string.IsNullOrEmpty(_vaultToken))
        {
            _logger.LogError("Vault token not found. Ensure VAULT_ROOT_TOKEN is set in the environment.");
            throw new InvalidOperationException("Vault token not found. Ensure VAULT_ROOT_TOKEN is set in the environment.");
        }

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _vaultToken);
    }

    public async Task<Dictionary<string, string>> FetchSecretsAsync(string secretPath, params string[] fields)
    {
        var secrets = new Dictionary<string, string>();
        var secretData = await GetSecretAsync(secretPath);

        foreach (var field in fields)
        {
            if (secretData[field] != null)
            {
                secrets[field] = secretData[field].ToString();
            }
            else
            {
                _logger.LogError($"Field '{field}' not found in the secret path '{secretPath}'");
            }
        }

        return secrets;
    }

    public async Task<JObject?> GetSecretAsync(string secretPath)
    {
        try
        {
            _logger.LogInformation("Fetching secret from path: {SecretPath}", secretPath);
            var response = await _httpClient.GetAsync($"/v1/{secretPath}");

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                return JObject.Parse(jsonResponse)["data"] as JObject;
            }

            _logger.LogError("Failed to fetch secret from path: {SecretPath}. Status code: {StatusCode}, Reason: {Reason}", 
                secretPath, response.StatusCode, response.ReasonPhrase);
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
