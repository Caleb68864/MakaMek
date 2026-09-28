namespace Sanet.MakaMek.Assets.Configuration;

/// <summary>
/// Default source for record sheet artwork, which lives in MegaMek's data repository rather than
/// this project's. Seeds a provider configuration rather than being consumed directly, so the
/// source can be repointed - at a mirror, a bucket, or a local checkout - without a code change.
/// </summary>
public static class MegaMekDefaults
{
    /// <summary>
    /// Root Contents API URL for the "data" folder of the MegaMek data repository. Per-asset-type
    /// subfolders are appended by <see cref="ResourceProviders.ResourceStreamProviderFactory"/>.
    /// </summary>
    public const string BaseUrl = "https://api.github.com/repos/MegaMek/mm-data/contents/data";
}
