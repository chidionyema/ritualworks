using Microsoft.Extensions.Configuration;
using  RitualWorks.Services;
public class VaultConfigurationSource : IConfigurationSource
{
    private readonly VaultService _vaultService;
    private readonly string _secretPath;
    private readonly string[] _secretKeys;

    public VaultConfigurationSource(VaultService vaultService, string secretPath, params string[] secretKeys)
    {
        _vaultService = vaultService;
        _secretPath = secretPath;
        _secretKeys = secretKeys;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new VaultConfigurationProvider(_vaultService, _secretPath, _secretKeys);
    }
}
