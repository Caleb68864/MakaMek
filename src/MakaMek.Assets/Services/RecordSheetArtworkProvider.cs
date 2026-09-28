using System.Collections.Concurrent;
using Sanet.MakaMek.Assets.ResourceProviders;

namespace Sanet.MakaMek.Assets.Services;

/// <summary>Looks up optional PNG artwork without logging when a unit has no illustration.</summary>
public sealed class RecordSheetArtworkProvider : IRecordSheetArtworkProvider
{
    private readonly Func<Task<IReadOnlyList<IResourceStreamProvider>>> _resolveProviders;
    private readonly ConcurrentDictionary<IResourceStreamProvider, Lazy<Task<IReadOnlyList<string>>>> _resourceIds = new();

    public RecordSheetArtworkProvider(IEnumerable<IResourceStreamProvider> providers)
    {
        var fixedProviders = providers.ToArray();
        _resolveProviders = () => Task.FromResult<IReadOnlyList<IResourceStreamProvider>>(fixedProviders);
    }

    /// <summary>
    /// Takes its sources from the asset provider configuration, so the artwork source is configured
    /// alongside units and hexes rather than fixed in code.
    /// </summary>
    public RecordSheetArtworkProvider(ConfiguredResourceProviders providers) =>
        _resolveProviders = providers.GetAsync;

    public async Task<Stream?> GetMechArtworkAsync(string mechName)
    {
        if (string.IsNullOrWhiteSpace(mechName) || Path.GetFileName(mechName) != mechName)
            return null;

        var fileName = mechName + ".png";
        foreach (var provider in await _resolveProviders())
        {
            try
            {
                var ids = await _resourceIds.GetOrAdd(provider, p =>
                    new Lazy<Task<IReadOnlyList<string>>>(() => LoadResourceIdsAsync(p))).Value;
                var id = ids.FirstOrDefault(candidate =>
                    string.Equals(GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase));
                if (id is null) continue;

                var stream = await provider.GetResourceStream(id);
                if (stream is not null) return stream;
            }
            catch
            {
                // Fluff art is optional. A broken or missing art source must not affect the sheet.
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<string>> LoadResourceIdsAsync(IResourceStreamProvider provider)
    {
        try
        {
            return (await provider.GetAvailableResourceIds()).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string GetFileName(string id)
    {
        var fileName = Uri.TryCreate(id, UriKind.Absolute, out var uri) && !uri.IsFile
            ? Path.GetFileName(uri.AbsolutePath)
            : Path.GetFileName(id);
        return Uri.UnescapeDataString(fileName);
    }

}
