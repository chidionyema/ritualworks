using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using  RitualWorks.Services;
public class VaultConfigurationProvider : ConfigurationProvider
{
    private readonly VaultService _vaultService;
    private readonly string _secretPath;
    private readonly string[] _secretKeys;

    public VaultConfigurationProvider(VaultService vaultService, string secretPath, params string[] secretKeys)
    {
        _vaultService = vaultService ?? throw new ArgumentNullException(nameof(vaultService));
        _secretPath = secretPath ?? throw new ArgumentNullException(nameof(secretPath));
        _secretKeys = secretKeys ?? throw new ArgumentNullException(nameof(secretKeys));
    }

    public override void Load()
    {
        LoadAsync().GetAwaiter().GetResult();
    }

    private async Task LoadAsync()
    {
        var secrets = await _vaultService.FetchSecretsAsync(_secretPath, _secretKeys);

        if (secrets != null)
        {
            foreach (var secret in secrets)
            {
                Data[secret.Key] = secret.Value;
            }
        }
    }
}
