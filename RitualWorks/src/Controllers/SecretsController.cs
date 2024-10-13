using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using RitualWorks.Services;
using System;
using System.Threading.Tasks;

namespace RitualWorks.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SecretsController : ControllerBase
    {
        private readonly VaultService _vaultService;

        // Constructor accepts VaultService, which will be injected by DI
        public SecretsController(VaultService vaultService)
        {
            _vaultService = vaultService ?? throw new ArgumentNullException(nameof(vaultService));
        }

        [HttpGet("{secretPath}")]
        public async Task<IActionResult> GetSecret(string secretPath)
        {
            // Construct the full path, considering the environment
            string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "dev";
            string fullSecretPath = $"secret/data/{environment}/{secretPath}";

            // Pass an empty array instead of null
            var secret = await _vaultService.FetchSecretsAsync(fullSecretPath, []);

            if (secret == null)
            {
                return NotFound("Secret not found or access denied.");
            }

            // Extract the data safely
            var secretData = secret.TryGetValue("data", out string? value) ? value : null;
            if (secretData == null)
            {
                return NotFound("No data found in the specified secret path.");
            }

            return Ok(secretData);
        }
    }
}
