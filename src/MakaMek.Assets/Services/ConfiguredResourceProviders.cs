using Sanet.MakaMek.Assets.Configuration;
using Sanet.MakaMek.Assets.ResourceProviders;

namespace Sanet.MakaMek.Assets.Services;

/// <summary>
/// Resolves the active providers for a set of asset types from configuration, once, on first use.
/// Configuration is read asynchronously and the DI container builds synchronously, so the lookup is
/// deferred rather than resolved at registration.
/// </summary>
public sealed class ConfiguredResourceProviders
{
    private readonly IAssetProviderConfigurationProvider _configuration;
    private readonly IResourceStreamProviderFactory _factory;
    private readonly AssetType[] _assetTypes;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<IResourceStreamProvider>? _resolved;

    public ConfiguredResourceProviders(
        IAssetProviderConfigurationProvider configuration,
        IResourceStreamProviderFactory factory,
        params AssetType[] assetTypes)
    {
        _configuration = configuration;
        _factory = factory;
        _assetTypes = assetTypes;
    }

    /// <summary>
    /// The active providers for the configured asset types, in configuration order. Re-resolved
    /// only if a previous attempt produced nothing, so a provider added in Settings is picked up
    /// without a restart.
    /// </summary>
    public async Task<IReadOnlyList<IResourceStreamProvider>> GetAsync()
    {
        if (_resolved is { Count: > 0 }) return _resolved;

        await _gate.WaitAsync();
        try
        {
            if (_resolved is { Count: > 0 }) return _resolved;

            var providers = new List<IResourceStreamProvider>();
            foreach (var assetType in _assetTypes)
            {
                foreach (var config in await _configuration.GetActiveProviders(assetType))
                    providers.Add(_factory.Create(config));
            }

            _resolved = providers;
            return _resolved;
        }
        finally
        {
            _gate.Release();
        }
    }
}
