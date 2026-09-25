namespace Sanet.MakaMek.Assets.Services;

/// <summary>Loads an unmodified record-sheet SVG from the configured asset providers.</summary>
public interface IRecordSheetTemplateProvider
{
    Task<Stream?> GetTemplateAsync(string templateName);

    Task<Stream?> GetPipClusterAsync(string clusterName);
}
