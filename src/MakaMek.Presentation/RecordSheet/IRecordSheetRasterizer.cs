namespace Sanet.MakaMek.Presentation.RecordSheet;

/// <summary>
/// Turns a composed record sheet SVG into PNG bytes. Separate from composition because rasterising
/// needs a drawing library, which the presentation layer deliberately does not depend on.
/// </summary>
public interface IRecordSheetRasterizer
{
    /// <summary>
    /// Rasterises <paramref name="svgBytes"/> at <paramref name="scale"/>, or returns null when the
    /// SVG cannot be drawn.
    /// </summary>
    byte[]? RasterizeToPng(byte[] svgBytes, float scale = 2f);
}
