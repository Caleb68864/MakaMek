namespace Sanet.MakaMek.Presentation.RecordSheet;

/// <summary>Builds a unit's record sheet as an SVG document.</summary>
public interface IRecordSheetComposer
{
    /// <summary>
    /// Composes the sheet for <paramref name="data"/>, optionally including fluff artwork.
    /// Returns null when the assets cannot produce a sheet, which is the caller's signal to fall
    /// back to the text record sheet.
    /// </summary>
    Task<byte[]?> ComposeAsync(
        RecordSheetDiagramData data, byte[]? artwork = null, CancellationToken cancellationToken = default);
}
