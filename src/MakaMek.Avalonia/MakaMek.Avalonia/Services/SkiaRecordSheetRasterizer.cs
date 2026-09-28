using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Sanet.MakaMek.Presentation.RecordSheet;
using SkiaSharp;
using Svg.Skia;

namespace Sanet.MakaMek.Avalonia.Services;

/// <summary>
/// Rasterises a composed record sheet with Svg.Skia. Kept out of the presentation layer because it
/// needs a drawing library; nothing here touches Avalonia, so it runs on any thread.
/// </summary>
public sealed class SkiaRecordSheetRasterizer : IRecordSheetRasterizer
{
    private readonly ILogger<SkiaRecordSheetRasterizer> _logger;

    public SkiaRecordSheetRasterizer(ILogger<SkiaRecordSheetRasterizer> logger) => _logger = logger;

    /// <inheritdoc />
    public RecordSheetImage? RasterizeToPng(byte[] svgBytes, float scale = 2f)
    {
        ArgumentNullException.ThrowIfNull(svgBytes);

        using var source = new MemoryStream(svgBytes, writable: false);
        using var drawing = new SKSvg();
        if (drawing.Load(source) is null)
        {
            _logger.LogWarning("Composed biped record-sheet SVG could not be parsed");
            return null;
        }

        var bounds = drawing.Picture?.CullRect ?? SKRect.Empty;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            _logger.LogWarning("Composed biped record-sheet SVG has no drawable area");
            return null;
        }

        using var png = new MemoryStream();
        drawing.Save(png, SKColors.White, SKEncodedImageFormat.Png, 100, scale, scale);
        return new RecordSheetImage(
            png.ToArray(),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale),
            scale);
    }
}
