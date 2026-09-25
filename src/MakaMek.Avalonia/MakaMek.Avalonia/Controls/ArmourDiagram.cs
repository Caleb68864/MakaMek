using System;
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
    private readonly IRecordSheetLayout _layout;
    private readonly ILogger<ArmourDiagram> _logger;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private RecordSheetViewModel? _viewModel;
    private long _renderGeneration;

    public ArmourDiagram(
        IRecordSheetTemplateProvider assets,
        IRecordSheetLayout layout,
        ILogger<ArmourDiagram> logger)
    {
        _assets = assets;
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
            _image.Source = null;
            _image.Width = double.NaN;
            _image.Height = double.NaN;
            return;
        }

        try
        {
            await using var template = await _assets.GetTemplateAsync("mek_biped_default.svg");
            if (template is null) return;

            var document = XDocument.Load(template);
            var root = document.Root;
            var armourPips = FindById(root, "canonArmorPips");
            var structurePips = FindById(root, "canonStructurePips");
            if (root is null || armourPips is null || structurePips is null)
            {
                _logger.LogWarning("Biped record-sheet template is missing a pip overlay layer");
                return;
            }

            AddCriticalSlotSeam(root);
            await AddArmourPipsAsync(document, armourPips, data);
            await AddStructurePipsAsync(document, structurePips, data);

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
                return;
            }

            using var png = new MemoryStream();
            svg.Save(png, SKColors.White, SKEncodedImageFormat.Png, 100, 2f, 2f);
            png.Position = 0;
            var renderedBitmap = new Bitmap(png);
            if (generation == Volatile.Read(ref _renderGeneration))
            {
                _image.Source = renderedBitmap;
                ResizeImage(Bounds.Width);
            }
            else
                renderedBitmap.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Record-sheet diagram could not be rendered");
        }
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecordSheetViewModel.DiagramData))
            _ = RenderCoreAsync(_viewModel?.DiagramData, Interlocked.Increment(ref _renderGeneration));
    }

    private async Task AddArmourPipsAsync(
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

                await AddClusterAsync(_assets.GetPipClusterAsync(clusterName), overlayLayer, regionId,
                    clusterName, null, partData);
            }
        }
    }

    private async Task AddStructurePipsAsync(
        XDocument document, XElement overlayLayer, RecordSheetDiagramData data)
    {
        foreach (var location in Enum.GetValues<PartLocation>())
        {
            var regionId = _layout.StructureRegionId(location);
            var clusterName = _layout.StructureClusterName(location, data.Tonnage);
            if (regionId is null || clusterName is null) continue;
            if (FindById(document.Root, regionId) is null)
            {
                _logger.LogWarning("Template region {RegionId} is missing", regionId);
                continue;
            }

            var partData = GetPartData(data, location);
            if (partData is not null)
                SetValueText(document, "textIS_" + regionId["isPips".Length..],
                    partData.CurrentStructure, StatusMarker(partData));

            await AddClusterAsync(_assets.GetPipClusterAsync(clusterName), overlayLayer, regionId,
                clusterName, document, partData);
        }
    }

    private async Task AddClusterAsync(
        Task<Stream?> streamTask,
        XElement overlayLayer,
        string regionId,
        string clusterName,
        XDocument? document,
        RecordSheetPartData? partData)
    {
        await using var stream = await streamTask;
        if (stream is null) return;

        try
        {
            var cluster = XDocument.Load(stream);
            var sourceSwitch = cluster.Root?.Element(SvgNamespace + "switch");
            if (sourceSwitch is null)
            {
                _logger.LogWarning("Pip cluster {ClusterName} has no SVG switch layer", clusterName);
                return;
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
            overlayLayer.Add(new XElement(SvgNamespace + "g",
                new XAttribute("data-template-region", regionId),
                new XAttribute("opacity", opacity),
                new XElement(sourceSwitch)));

            if (document is not null && partData is null)
                SetValueText(document, "textIS_" + regionId["isPips".Length..],
                    pipCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pip cluster {ClusterName} could not be composed", clusterName);
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
