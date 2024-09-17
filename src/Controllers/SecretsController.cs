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

            var secret = await _vaultService.GetSecretAsync(fullSecretPath);

            if (secret == null)
            {
                return NotFound("Secret not found or access denied.");
            }

            var secretData = secret["data"]?["data"];
            if (secretData == null)
            {
                return NotFound("No data found in the specified secret path.");
            }

            return Ok(secretData);
        }
    }
}
