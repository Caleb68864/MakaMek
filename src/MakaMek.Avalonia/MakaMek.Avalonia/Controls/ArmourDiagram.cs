using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
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

    private readonly IRecordSheetComposer _composer;
    private readonly IRecordSheetArtworkProvider? _artworkProvider;
    private readonly IRecordSheetRasterizer _rasterizer;
    private readonly ILogger<ArmourDiagram> _logger;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private bool _isSubscribed = true;
    private RecordSheetViewModel? _viewModel;
    private long _renderGeneration;
    private RecordSheetDiagramData? _latestData;
    private bool _isTemplateAvailable;
    private string? _artworkCacheKey;
    private byte[]? _artworkBytes;
    private bool _artworkLookupAttempted;



    public event EventHandler<bool>? TemplateAvailabilityChanged;

    public ArmourDiagram(
        IRecordSheetComposer composer,
        IRecordSheetRasterizer rasterizer,
        ILogger<ArmourDiagram> logger)
        : this(composer, rasterizer, logger, null)
    {
    }

    public ArmourDiagram(
        IRecordSheetComposer composer,
        IRecordSheetRasterizer rasterizer,
        ILogger<ArmourDiagram> logger,
        IRecordSheetArtworkProvider? artworkProvider)
    {
        _composer = composer;
        _rasterizer = rasterizer;
        _artworkProvider = artworkProvider;
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
        if (_viewModel is not null && _isSubscribed)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        QueueViewModelRender(_viewModel?.DiagramData);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_isSubscribed || _viewModel is null) return;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _isSubscribed = true;
        QueueViewModelRender(_viewModel.DiagramData);
    }

    /// <summary>
    /// The view model outlives this control, so staying subscribed would keep the control and its
    /// rendered sheet alive after the view is gone. The bitmap goes with it.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _isSubscribed = false;
        Interlocked.Increment(ref _renderGeneration);
        SetImageSource(null);
        base.OnDetachedFromVisualTree(e);
    }

    public Task RenderAsync(RecordSheetDiagramData data)
    {
        _latestData = data;
        return RenderCoreAsync(data, Interlocked.Increment(ref _renderGeneration));
    }

    private void QueueViewModelRender(RecordSheetDiagramData? data)
    {
        _latestData = data;
        var generation = Interlocked.Increment(ref _renderGeneration);
        if (data is null)
        {
            _ = RenderCoreAsync(null, generation);
            return;
        }

        Dispatcher.Post(() =>
        {
            if (generation == Volatile.Read(ref _renderGeneration))
                _ = RenderCoreAsync(data, generation);
        }, DispatcherPriority.Background);
    }

    private async Task RenderCoreAsync(RecordSheetDiagramData? data, long generation)
    {
        if (generation != Volatile.Read(ref _renderGeneration)) return;
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
            // Snapshot rather than letting the background work read fields off the UI thread.
            var artwork = _artworkBytes;

            // Composing the sheet and rasterising it touch no Avalonia object, so they run off the
            // UI thread; only the finished bitmap comes back, and awaiting returns us to the UI
            // thread through Avalonia's synchronization context.
            var rendered = await Task.Run<Bitmap?>(async () =>
            {
                var svg = await _composer.ComposeAsync(data, artwork);
                if (svg is null) return null;
                var image = _rasterizer.RasterizeToPng(svg);
                return image is null ? null : new Bitmap(new MemoryStream(image.PngBytes, writable: false));
            });

            if (generation != Volatile.Read(ref _renderGeneration))
            {
                rendered?.Dispose();
                return;
            }

            if (rendered is null)
            {
                SetTemplateAvailability(false);
                return;
            }

            SetImageSource(rendered);
            ResizeImage(Bounds.Width);
            SetTemplateAvailability(true);
            StartArtworkLookup(data);
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
        if (!isAvailable)
            SetImageSource(null);
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
            QueueViewModelRender(_viewModel?.DiagramData);
    }

    private void StartArtworkLookup(RecordSheetDiagramData data)
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
                _ = RenderCoreAsync(_latestData,
                    Interlocked.Increment(ref _renderGeneration));
            });
        }
        catch
        {
            // Optional artwork must never make the record sheet unavailable.
        }
    }

}
