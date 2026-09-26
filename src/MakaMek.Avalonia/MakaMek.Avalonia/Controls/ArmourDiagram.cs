using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Sanet.MakaMek.Assets.Services;
using Sanet.MakaMek.Core.Models.Units;
using Sanet.MakaMek.Presentation.RecordSheet;
using SkiaSharp;
using Svg.Skia;

namespace Sanet.MakaMek.Avalonia.Controls;

/// <summary>Composes an unmodified MegaMek sheet with value-selected pip artwork.</summary>
public sealed class ArmourDiagram : UserControl
{
    private const double SheetMargin = 12;

    public static readonly StyledProperty<RecordSheetViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<ArmourDiagram, RecordSheetViewModel?>(nameof(ViewModel));

    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";
    private readonly IRecordSheetTemplateProvider _assets;
    private readonly IRecordSheetArtworkProvider? _artworkProvider;
    private readonly IRecordSheetLayout _layout;
    private readonly ILogger<ArmourDiagram> _logger;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private RecordSheetViewModel? _viewModel;
    private long _renderGeneration;
    private bool _isTemplateAvailable;
    private string? _artworkCacheKey;
    private byte[]? _artworkBytes;
    private bool _artworkLookupAttempted;

    public event EventHandler<bool>? TemplateAvailabilityChanged;

    public ArmourDiagram(
        IRecordSheetTemplateProvider assets,
        IRecordSheetLayout layout,
        ILogger<ArmourDiagram> logger)
        : this(assets, layout, logger, null)
    {
    }

    public ArmourDiagram(
        IRecordSheetTemplateProvider assets,
        IRecordSheetLayout layout,
        ILogger<ArmourDiagram> logger,
        IRecordSheetArtworkProvider? artworkProvider)
    {
        _assets = assets;
        _artworkProvider = artworkProvider;
        _layout = layout;
        _logger = logger;
        Content = new ScrollViewer
        {
            Background = Brushes.White,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border
            {
                Padding = new Thickness(SheetMargin),
                Child = _image
            }
        };
        SizeChanged += OnSizeChanged;
    }

    public RecordSheetViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ViewModelProperty) return;

        if (change.GetOldValue<RecordSheetViewModel?>() is { } oldViewModel)
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = change.GetNewValue<RecordSheetViewModel?>();
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _ = RenderCoreAsync(_viewModel?.DiagramData, Interlocked.Increment(ref _renderGeneration));
    }

    public Task RenderAsync(RecordSheetDiagramData data) => RenderCoreAsync(data, Interlocked.Increment(ref _renderGeneration));

    private async Task RenderCoreAsync(RecordSheetDiagramData? data, long generation)
    {
        if (data is null)
        {
            _artworkCacheKey = null;
            _artworkBytes = null;
            _artworkLookupAttempted = false;
            SetImageSource(null);
            _image.Width = double.NaN;
            _image.Height = double.NaN;
            SetTemplateAvailability(false);
            return;
        }

        if (!string.Equals(_artworkCacheKey, data.FluffArtworkName, StringComparison.Ordinal))
        {
            _artworkCacheKey = data.FluffArtworkName;
            _artworkBytes = null;
            _artworkLookupAttempted = false;
        }

        try
        {
            await using var template = await _assets.GetTemplateAsync("mek_biped_default.svg");
            if (template is null)
            {
                if (generation == Volatile.Read(ref _renderGeneration))
                    SetTemplateAvailability(false);
                return;
            }

            var document = XDocument.Load(template);
            var root = document.Root;
            var armourPips = FindById(root, "canonArmorPips");
            var structurePips = FindById(root, "canonStructurePips");
            if (root is null || armourPips is null || structurePips is null)
            {
                _logger.LogWarning("Biped record-sheet template is missing a pip overlay layer");
                if (generation == Volatile.Read(ref _renderGeneration))
                    SetTemplateAvailability(false);
                return;
            }

            AddCriticalSlotSeam(root);
            AddArtworkSeam(root);
            var hasAllArmourPips = await AddArmourPipsAsync(document, armourPips, data);
            var hasAllStructurePips = await AddStructurePipsAsync(document, structurePips, data);
            if (!hasAllArmourPips || !hasAllStructurePips)
            {
                if (generation == Volatile.Read(ref _renderGeneration))
                    SetTemplateAvailability(false);
                return;
            }
            AddCriticalSlots(document, FindById(root, "criticalSlotOverlay"), data);
            AddFluffArtwork(document, FindById(root, "recordSheetArtworkOverlay"), data, _artworkBytes);

            using var svgStream = new MemoryStream();
            using (var writer = XmlWriter.Create(svgStream, new XmlWriterSettings
                   {
                       Encoding = new UTF8Encoding(false),
                       OmitXmlDeclaration = false
                   }))
            {
                document.Save(writer);
            }
            svgStream.Position = 0;

            using var svg = new SKSvg();
            if (svg.Load(svgStream) is null)
            {
                _logger.LogWarning("Composed biped record-sheet SVG could not be parsed");
                if (generation == Volatile.Read(ref _renderGeneration))
                    SetTemplateAvailability(false);
                return;
            }

            using var png = new MemoryStream();
            svg.Save(png, SKColors.White, SKEncodedImageFormat.Png, 100, 2f, 2f);
            png.Position = 0;
            var renderedBitmap = new Bitmap(png);
            if (generation == Volatile.Read(ref _renderGeneration))
            {
                SetImageSource(renderedBitmap);
                ResizeImage(Bounds.Width);
                SetTemplateAvailability(true);
                StartArtworkLookup(data, generation);
            }
            else
                renderedBitmap.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Record-sheet diagram could not be rendered");
            if (generation == Volatile.Read(ref _renderGeneration))
                SetTemplateAvailability(false);
        }
    }

    private void SetTemplateAvailability(bool isAvailable)
    {
        if (_isTemplateAvailable == isAvailable) return;
        _isTemplateAvailable = isAvailable;
        TemplateAvailabilityChanged?.Invoke(this, isAvailable);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => ResizeImage(e.NewSize.Width);

    private void ResizeImage(double availableWidth)
    {
        if (_image.Source is not Bitmap bitmap || availableWidth <= SheetMargin * 2)
            return;

        var width = availableWidth - SheetMargin * 2;
        _image.Width = width;
        _image.Height = width * bitmap.PixelSize.Height / bitmap.PixelSize.Width;
    }

    private void SetImageSource(Bitmap? source)
    {
        var previous = _image.Source as Bitmap;
        _image.Source = source;
        if (!ReferenceEquals(previous, source))
            previous?.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecordSheetViewModel.DiagramData))
            _ = RenderCoreAsync(_viewModel?.DiagramData, Interlocked.Increment(ref _renderGeneration));
    }

    private static void AddArtworkSeam(XElement root)
    {
        if (FindById(root, "recordSheetArtworkOverlay") is not null) return;
        root.Add(new XElement(SvgNamespace + "g", new XAttribute("id", "recordSheetArtworkOverlay")));
    }

    private static void AddFluffArtwork(
        XDocument document, XElement? overlayLayer, RecordSheetDiagramData data, byte[]? artworkBytes)
    {
        if (overlayLayer is null || artworkBytes is null || artworkBytes.Length == 0 ||
            data.FluffArtworkName is null)
            return;

        var root = document.Root;
        var region = FindById(root, "fluffSinglePilot");
        if (root is null || region is null || !TryGetRegionBounds(region, out var x, out var y, out var width, out var height))
            return;

        const double inset = 1;
        var href = "data:image/png;base64," + Convert.ToBase64String(artworkBytes);
        overlayLayer.Add(new XElement(SvgNamespace + "image",
            new XAttribute("data-record-sheet-artwork", data.FluffArtworkName),
            new XAttribute("x", Number(x + inset)),
            new XAttribute("y", Number(y + inset)),
            new XAttribute("width", Number(Math.Max(0, width - inset * 2))),
            new XAttribute("height", Number(Math.Max(0, height - inset * 2))),
            new XAttribute("preserveAspectRatio", "xMidYMid meet"),
            new XAttribute("href", href),
            GetAncestorTransforms(region, root) is { } transform ? new XAttribute("transform", transform) : null));
    }

    private void StartArtworkLookup(RecordSheetDiagramData data, long generation)
    {
        if (OperatingSystem.IsAndroid() || _artworkProvider is null || _artworkLookupAttempted ||
            string.IsNullOrWhiteSpace(data.FluffArtworkName))
            return;

        _artworkLookupAttempted = true;
        _ = LoadArtworkAsync(data);
    }

    private async Task LoadArtworkAsync(RecordSheetDiagramData data)
    {
        try
        {
            var mechName = data.FluffArtworkName!;
            await using var stream = await _artworkProvider!.GetMechArtworkAsync(mechName);
            if (stream is null) return;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            if (buffer.Length == 0 ||
                !string.Equals(_artworkCacheKey, mechName, StringComparison.Ordinal))
                return;

            var artworkBytes = buffer.ToArray();
            Dispatcher.UIThread.Post(() =>
            {
                if (!string.Equals(_artworkCacheKey, mechName, StringComparison.Ordinal)) return;
                _artworkBytes = artworkBytes;
                _ = RenderCoreAsync(_viewModel?.DiagramData ?? data,
                    Interlocked.Increment(ref _renderGeneration));
            });
        }
        catch
        {
            // Optional artwork must never make the record sheet unavailable.
        }
    }

    private async Task<bool> AddArmourPipsAsync(
        XDocument document, XElement overlayLayer, RecordSheetDiagramData data)
    {
        foreach (var location in Enum.GetValues<PartLocation>())
        {
            foreach (var face in new[] { ArmourFace.Front, ArmourFace.Rear })
            {
                var regionId = _layout.TemplateRegionId(location, face);
                if (regionId is null) continue;

                var region = FindById(document.Root, regionId);
                if (region is null)
                {
                    _logger.LogWarning("Template region {RegionId} is missing", regionId);
                    continue;
                }

                data.Armour.TryGetValue(new ArmourRegion(location, face), out var value);
                var partData = GetPartData(data, location);
                SetValueText(document, "textArmor_" + regionId["armorPips".Length..], value,
                    StatusMarker(partData));
                var clusterName = _layout.ArmourClusterName(location, face, value);
                if (clusterName is null) continue;

                var useGeneratedFallback = value > GetMaximumArmourPipValue(location, face);
                var hasCluster = await AddClusterAsync(_assets.GetPipClusterAsync(clusterName), overlayLayer,
                    regionId, clusterName, location, null, partData,
                    data.RecentlyDamagedLocations.Contains(location));
                if (hasCluster) continue;

                if (useGeneratedFallback)
                {
                    if (!AddGeneratedFallbackPips(document, region, regionId, value,
                        partData, data.RecentlyDamagedLocations.Contains(location), location,
                        "armour", clusterName))
                        return false;
                    continue;
                }

                return false;
            }
        }

        return true;
    }

    private async Task<bool> AddStructurePipsAsync(
        XDocument document, XElement overlayLayer, RecordSheetDiagramData data)
    {
        foreach (var location in Enum.GetValues<PartLocation>())
        {
            var regionId = _layout.StructureRegionId(location);
            var clusterName = _layout.StructureClusterName(location, data.Tonnage);
            if (regionId is null || clusterName is null) continue;
            var region = FindById(document.Root, regionId);
            if (region is null)
            {
                _logger.LogWarning("Template region {RegionId} is missing", regionId);
                continue;
            }

            var partData = GetPartData(data, location);
            if (partData is not null)
                SetValueText(document, "textIS_" + regionId["isPips".Length..],
                    partData.CurrentStructure, StatusMarker(partData));

            if (data.Tonnage > 100)
            {
                if (!await AddClusterAsync(_assets.GetPipClusterAsync(clusterName), overlayLayer, regionId,
                        clusterName, location, document, partData,
                        data.RecentlyDamagedLocations.Contains(location)) &&
                    !AddGeneratedFallbackPips(document, region, regionId,
                        partData?.CurrentStructure ?? 0, partData,
                        data.RecentlyDamagedLocations.Contains(location), location,
                        "internal structure", clusterName))
                    return false;
                continue;
            }

            if (!await AddClusterAsync(_assets.GetPipClusterAsync(clusterName), overlayLayer, regionId,
                    clusterName, location, document, partData, data.RecentlyDamagedLocations.Contains(location)))
                return false;
        }

        return true;
    }

    private bool AddGeneratedFallbackPips(
        XDocument document,
        XElement region,
        string regionId,
        int value,
        RecordSheetPartData? partData,
        bool recentlyDamaged,
        PartLocation location,
        string pipKind,
        string requestedCluster)
    {
        if (value <= 0) return true;

        var root = document.Root;
        if (root is null) return false;

        var rows = region.Descendants(SvgNamespace + "rect")
            .Where(row => TryGetNumber(row, "x", out _) && TryGetNumber(row, "y", out _) &&
                          TryGetNumber(row, "width", out var width) && width > 0 &&
                          TryGetNumber(row, "height", out var height) && height > 0)
            .ToArray();
        if (rows.Length == 0)
        {
            _logger.LogWarning("Cannot generate fallback pips for template region {RegionId}", regionId);
            return false;
        }

        var remaining = value;
        var columns = (int)Math.Ceiling((double)value / rows.Length);
        var opacity = GetPartOpacity(partData, pipKind == "internal structure");
        foreach (var row in rows)
        {
            if (remaining <= 0) break;
            var x = ParseNumber(row, "x");
            var y = ParseNumber(row, "y");
            var width = ParseNumber(row, "width");
            var height = ParseNumber(row, "height");
            var count = Math.Min(columns, remaining);
            var radius = Math.Min(2.2, Math.Min(height * 0.34, width / count * 0.34));
            var parent = row.Parent ?? region;
            var rowGroup = new XElement(SvgNamespace + "g",
                new XAttribute("data-template-region", regionId),
                new XAttribute("data-generated-fallback", pipKind),
                new XAttribute("opacity", opacity),
                recentlyDamaged ? new XAttribute("data-recent-damage", location.ToString()) : null,
                recentlyDamaged ? new XAttribute("stroke", "#e4572e") : null,
                recentlyDamaged ? new XAttribute("stroke-width", "1.5") : null,
                GetAncestorTransforms(parent, root) is { } transform ? new XAttribute("transform", transform) : null);

            for (var column = 0; column < count; column++)
            {
                var centerX = x + width * (column + 0.5) / count;
                rowGroup.Add(new XElement(SvgNamespace + "circle",
                    new XAttribute("data-generated-pip", value - remaining + column),
                    new XAttribute("cx", Number(centerX)),
                    new XAttribute("cy", Number(y + height / 2)),
                    new XAttribute("r", Number(radius)),
                    new XAttribute("fill", "none"),
                    new XAttribute("stroke", recentlyDamaged ? "#e4572e" : "#000000"),
                    new XAttribute("stroke-width", "0.5")));
            }

            // The imported MegaMek clusters live under a compensating overlay transform.
            // Generated positions are already expressed in template coordinates, so append
            // them at the SVG root to avoid applying that transform a second time.
            root.Add(rowGroup);
            remaining -= count;
        }

        if (remaining > 0)
        {
            _logger.LogWarning("Template region {RegionId} could not fit {Remaining} generated fallback pips",
                regionId, remaining);
            return false;
        }

        return true;
    }

    private static double ParseNumber(XElement element, string attribute)
    {
        TryGetNumber(element, attribute, out var value);
        return value;
    }

    private static string GetPartOpacity(RecordSheetPartData? partData, bool isStructure) => partData switch
    {
        { IsBlownOff: true } => "0.04",
        { IsDestroyed: true } => "0.3",
        { MaxStructure: > 0 } when isStructure =>
            Math.Clamp((double)partData.CurrentStructure / partData.MaxStructure, 0.15, 1)
                .ToString("0.###", CultureInfo.InvariantCulture),
        _ => "1"
    };

    private static int GetMaximumArmourPipValue(PartLocation location, ArmourFace face) => (location, face) switch
    {
        (PartLocation.Head, ArmourFace.Front) => 9,
        (PartLocation.LeftArm or PartLocation.RightArm, ArmourFace.Front) => 34,
        (PartLocation.LeftLeg or PartLocation.RightLeg, ArmourFace.Front) => 43,
        (PartLocation.CenterTorso, ArmourFace.Front) => 51,
        (PartLocation.LeftTorso or PartLocation.RightTorso, ArmourFace.Front) => 34,
        (PartLocation.CenterTorso, ArmourFace.Rear) => 21,
        (PartLocation.LeftTorso or PartLocation.RightTorso, ArmourFace.Rear) => 15,
        _ => 0
    };

    private void AddCriticalSlots(
        XDocument document,
        XElement? overlayLayer,
        RecordSheetDiagramData data)
    {
        if (overlayLayer is null) return;
        var root = document.Root;
        if (root is null) return;

        foreach (var location in Enum.GetValues<PartLocation>())
        {
            var regionId = _layout.CriticalSlotRegionId(location);
            if (regionId is null) continue;

            var region = FindById(document.Root, regionId);
            if (region is null || !TryGetRegionBounds(region, out var x, out var y, out var width, out var height))
            {
                _logger.LogWarning("Critical-slot region {RegionId} is missing or has no rectangular bounds", regionId);
                continue;
            }

            if (!data.CriticalSlots.TryGetValue(location, out var slots))
            {
                overlayLayer.Add(CreateMissingLocationMarker(
                    regionId, x, y, width, height, GetAncestorTransforms(region, root)));
                continue;
            }

            AddCriticalSlotCells(
                overlayLayer, regionId, slots, x, y, width, height, GetAncestorTransforms(region, root));
        }
    }

    private static void AddCriticalSlotCells(
        XElement overlayLayer,
        string regionId,
        IReadOnlyList<RecordSheetCriticalSlotData> slots,
        double x,
        double y,
        double width,
        double height,
        string? transform)
    {
        if (slots.Count == 0) return;

        const double margin = 1.25;
        const double centerGap = 1;
        var rows = (slots.Count + 1) / 2;
        var cellWidth = (width - (margin * 2) - centerGap) / 2;
        var cellHeight = (height - (margin * 2)) / rows;
        var slotGroup = new XElement(SvgNamespace + "g",
            new XAttribute("data-template-region", regionId),
            transform is null ? null : new XAttribute("transform", transform));

        foreach (var slot in slots)
        {
            // The template presents critical slots as two vertical groups of six.
            var column = slot.Slot / rows;
            var row = slot.Slot % rows;
            if (column > 1) continue;

            var cellX = x + margin + column * (cellWidth + centerGap);
            var cellY = y + margin + row * cellHeight;
            var style = GetCriticalSlotStyle(slot.State);
            slotGroup.Add(new XElement(SvgNamespace + "rect",
                new XAttribute("data-critical-slot", slot.Slot),
                new XAttribute("data-slot-state", slot.State.ToString()),
                new XAttribute("x", Number(cellX)),
                new XAttribute("y", Number(cellY)),
                new XAttribute("width", Number(cellWidth)),
                new XAttribute("height", Number(cellHeight)),
                new XAttribute("fill", style.Fill),
                new XAttribute("stroke", style.Stroke),
                new XAttribute("stroke-width", "0.45")));

            slotGroup.Add(new XElement(SvgNamespace + "text",
                new XAttribute("x", Number(cellX + 1)),
                new XAttribute("y", Number(cellY + 3.1)),
                new XAttribute("font-size", "2.6"),
                new XAttribute("fill", "#666666"),
                new XAttribute("data-slot-number", slot.Slot + 1),
                (slot.Slot + 1).ToString(CultureInfo.InvariantCulture)));

            if (slot.State == CriticalSlotState.Empty) continue;
            var label = slot.State == CriticalSlotState.Destroyed ? "X " + slot.ComponentName : slot.ComponentName;
            if (string.IsNullOrWhiteSpace(label)) continue;

            var textWidth = Math.Min(cellWidth - 4, Math.Max(4, label.Length * 2.15));
            slotGroup.Add(new XElement(SvgNamespace + "text",
                new XAttribute("x", Number(cellX + 4)),
                new XAttribute("y", Number(cellY + cellHeight * 0.7)),
                new XAttribute("font-size", "4"),
                new XAttribute("font-weight", slot.State == CriticalSlotState.Intact ? "normal" : "bold"),
                new XAttribute("fill", style.Text),
                new XAttribute("textLength", Number(textWidth)),
                new XAttribute("lengthAdjust", "spacingAndGlyphs"),
                label));
        }

        overlayLayer.Add(slotGroup);
    }

    private static XElement CreateMissingLocationMarker(
        string regionId,
        double x,
        double y,
        double width,
        double height,
        string? transform) =>
        new(SvgNamespace + "g",
            new XAttribute("data-template-region", regionId),
            new XAttribute("data-missing-location", "true"),
            transform is null ? null : new XAttribute("transform", transform),
            new XElement(SvgNamespace + "rect",
                new XAttribute("x", Number(x)),
                new XAttribute("y", Number(y)),
                new XAttribute("width", Number(width)),
                new XAttribute("height", Number(height)),
                new XAttribute("fill", "#f7e2e2"),
                new XAttribute("stroke", "#9b2c2c"),
                new XAttribute("stroke-width", "1")),
            new XElement(SvgNamespace + "path",
                new XAttribute("d", $"M {Number(x)} {Number(y)} L {Number(x + width)} {Number(y + height)} M {Number(x + width)} {Number(y)} L {Number(x)} {Number(y + height)}"),
                new XAttribute("stroke", "#9b2c2c"),
                new XAttribute("stroke-width", "1")));

    private static string? GetAncestorTransforms(XElement element, XElement root)
    {
        var transforms = element.Ancestors()
            .Where(ancestor => !ReferenceEquals(ancestor, root))
            .Reverse()
            .Select(ancestor => (string?)ancestor.Attribute("transform"))
            .Where(transform => !string.IsNullOrWhiteSpace(transform));
        var result = string.Join(" ", transforms);
        return result.Length == 0 ? null : result;
    }

    private static (string Fill, string Stroke, string Text) GetCriticalSlotStyle(CriticalSlotState state) => state switch
    {
        CriticalSlotState.Hit => ("#fff1c2", "#bd7a00", "#8a4b00"),
        CriticalSlotState.Destroyed => ("#f3d1d1", "#a52a2a", "#8b1d1d"),
        CriticalSlotState.Empty => ("#f4f4f4", "#777777", "#333333"),
        _ => ("#ffffff", "#777777", "#111111")
    };

    private static bool TryGetRegionBounds(
        XElement region,
        out double x,
        out double y,
        out double width,
        out double height)
    {
        x = y = width = height = 0;
        return TryGetNumber(region, "x", out x) && TryGetNumber(region, "y", out y) &&
               TryGetNumber(region, "width", out width) && TryGetNumber(region, "height", out height);
    }

    private static bool TryGetNumber(XElement element, string attribute, out double value) =>
        double.TryParse((string?)element.Attribute(attribute), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value);

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private async Task<bool> AddClusterAsync(
        Task<Stream?> streamTask,
        XElement overlayLayer,
        string regionId,
        string clusterName,
        PartLocation location,
        XDocument? document,
        RecordSheetPartData? partData,
        bool recentlyDamaged)
    {
        await using var stream = await streamTask;
        if (stream is null) return false;

        try
        {
            var cluster = XDocument.Load(stream);
            var sourceSwitch = cluster.Root?.Element(SvgNamespace + "switch");
            if (sourceSwitch is null)
            {
                _logger.LogWarning("Pip cluster {ClusterName} has no SVG switch layer", clusterName);
                return false;
            }

            var pipCount = sourceSwitch.Descendants(SvgNamespace + "path").Count();
            var opacity = partData switch
            {
                { IsBlownOff: true } => "0.04",
                { IsDestroyed: true } => "0.3",
                { MaxStructure: > 0 } when document is not null =>
                    Math.Clamp((double)partData.CurrentStructure / partData.MaxStructure, 0.15, 1)
                        .ToString("0.###", CultureInfo.InvariantCulture),
                _ => "1"
            };
            var renderedSwitch = new XElement(sourceSwitch);
            if (recentlyDamaged)
            {
                foreach (var path in renderedSwitch.Descendants(SvgNamespace + "path"))
                {
                    path.SetAttributeValue("stroke", "#e4572e");
                    path.SetAttributeValue("stroke-width", "1.5");
                }
            }

            overlayLayer.Add(new XElement(SvgNamespace + "g",
                new XAttribute("data-template-region", regionId),
                recentlyDamaged ? new XAttribute("data-recent-damage", location.ToString()) : null,
                new XAttribute("opacity", opacity),
                renderedSwitch));

            if (document is not null && partData is null)
                SetValueText(document, "textIS_" + regionId["isPips".Length..],
                    pipCount);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pip cluster {ClusterName} could not be composed", clusterName);
            return false;
        }
    }

    private static void SetValueText(XDocument document, string id, int value, string statusMarker = "")
    {
        var text = FindById(document.Root, id);
        if (text is not null)
            text.Value = $"( {value.ToString(CultureInfo.InvariantCulture)} ){statusMarker}";
    }

    private static string StatusMarker(RecordSheetPartData? partData) => partData switch
    {
        { IsBlownOff: true } => " ×",
        { IsDestroyed: true } => " †",
        _ => string.Empty
    };

    private static RecordSheetPartData? GetPartData(RecordSheetDiagramData data, PartLocation location) =>
        data.Locations.TryGetValue(location, out var partData) ? partData : null;

    private static XElement? FindById(XElement? root, string id) =>
        root?.DescendantsAndSelf().FirstOrDefault(element => (string?)element.Attribute("id") == id);

    private static void AddCriticalSlotSeam(XElement root)
    {
        if (FindById(root, "criticalSlotOverlay") is null)
            root.Add(new XElement(SvgNamespace + "g", new XAttribute("id", "criticalSlotOverlay")));
    }
}
