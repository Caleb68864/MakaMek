using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sanet.MakaMek.Assets.ResourceProviders;

namespace Sanet.MakaMek.Assets.Services;

/// <summary>Finds record sheets by file name in the existing resource-stream provider stack.</summary>
public sealed class RecordSheetTemplateProvider : IRecordSheetTemplateProvider
{
    private readonly IReadOnlyList<IResourceStreamProvider> _providers;
    private readonly ILogger<RecordSheetTemplateProvider> _logger;
    private readonly HashSet<string> _loggedMissing = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<IResourceStreamProvider, Lazy<Task<IReadOnlyList<string>>>> _resourceIds = new();
    private readonly object _logLock = new();
    private readonly ConcurrentDictionary<string, byte[]> _assetCache = new(StringComparer.OrdinalIgnoreCase);

    public RecordSheetTemplateProvider(
        IEnumerable<IResourceStreamProvider> providers,
        ILogger<RecordSheetTemplateProvider> logger)
    {
        _providers = providers.ToArray();
        _logger = logger;
    }

    public Task<Stream?> GetTemplateAsync(string templateName) => GetAssetAsync(templateName);

    public Task<Stream?> GetPipClusterAsync(string clusterName) => GetAssetAsync(clusterName);

    private async Task<Stream?> GetAssetAsync(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName) ||
            !assetName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(assetName) != assetName)
        {
            LogMissingOnce(assetName ?? string.Empty);
            return null;
        }

        if (_assetCache.TryGetValue(assetName, out var cached))
            return new MemoryStream(cached, writable: false);

        foreach (var provider in _providers)
        {
            try
            {
                var ids = await _resourceIds.GetOrAdd(provider, p =>
                    new Lazy<Task<IReadOnlyList<string>>>(() => LoadResourceIdsAsync(p))).Value;
                var id = ids.FirstOrDefault(candidate =>
                    string.Equals(GetFileName(candidate), assetName, StringComparison.OrdinalIgnoreCase));
                if (id == null) continue;

                await using var stream = await provider.GetResourceStream(id);
                if (stream is null) continue;
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                var bytes = buffer.ToArray();
                _assetCache[assetName] = bytes;
                return new MemoryStream(bytes, writable: false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Record-sheet provider {ProviderId} failed", provider.Id);
            }
        }

        LogMissingOnce(assetName);
        return null;
    }

    private async Task<IReadOnlyList<string>> LoadResourceIdsAsync(IResourceStreamProvider provider)
    {
        try
        {
            return (await provider.GetAvailableResourceIds()).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Record-sheet provider {ProviderId} could not list assets", provider.Id);
            return [];
        }
    }

    /// <summary>Uses an unmodified mm-data checkout without a bucket or packaging step.</summary>
    public static RecordSheetTemplateProvider FromLocalCheckout(
        string mmDataRoot, ILogger<RecordSheetTemplateProvider> logger)
    {
        var recordSheets = Path.Combine(mmDataRoot, "data", "images", "recordsheets");
        return new RecordSheetTemplateProvider(
            [
                new LocalFolderResourceStreamProvider(Path.Combine(recordSheets, "templates_us"), "svg", "mm-data-record-sheets"),
                new LocalFolderResourceStreamProvider(Path.Combine(recordSheets, "biped_pips"), "svg", "mm-data-record-sheet-pips")
            ], logger);
    }

    private static string GetFileName(string id)
    {
        if (Uri.TryCreate(id, UriKind.Absolute, out var uri) && !uri.IsFile)
            return Path.GetFileName(uri.AbsolutePath);
        return Path.GetFileName(id);
    }

    private void LogMissingOnce(string name)
    {
        lock (_logLock)
        {
            if (_loggedMissing.Add(name))
                _logger.LogWarning("Record-sheet template {TemplateName} was not found", name);
        }
    }
}
