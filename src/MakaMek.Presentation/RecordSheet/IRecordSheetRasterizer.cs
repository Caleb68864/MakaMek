namespace Sanet.MakaMek.Presentation.RecordSheet;

/// <summary>
/// A rasterised record sheet. <paramref name="Scale"/> is the factor the sheet was drawn at, so a
/// caller that needs the sheet's natural size - a page size, for instance - can divide by it.
/// </summary>
public sealed record RecordSheetImage(byte[] PngBytes, int WidthPixels, int HeightPixels, float Scale);

/// <summary>
/// Turns a composed record sheet SVG into a PNG. Separate from composition because rasterising
/// needs a drawing library, which the presentation layer deliberately does not depend on.
/// </summary>
public interface IRecordSheetRasterizer
{
    /// <summary>
    /// Rasterises <paramref name="svgBytes"/> at <paramref name="scale"/>, or returns null when the
    /// SVG cannot be drawn.
    /// </summary>
    RecordSheetImage? RasterizeToPng(byte[] svgBytes, float scale = 2f);
}
