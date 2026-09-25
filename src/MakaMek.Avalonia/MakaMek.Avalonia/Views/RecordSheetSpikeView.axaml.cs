using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanet.MakaMek.Assets.Services;
using Sanet.MakaMek.Avalonia.Controls;
using Sanet.MakaMek.Presentation.RecordSheet;

namespace Sanet.MakaMek.Avalonia.Views;

/// <summary>Temporary debug surface for the unmodified MegaMek biped record sheet.</summary>
public partial class RecordSheetSpikeView : UserControl
{
    private bool _loadStarted;

    public RecordSheetSpikeView()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) => await LoadSheetAsync();
    }

    private async Task LoadSheetAsync()
    {
        if (_loadStarted) return;
        _loadStarted = true;

        try
        {
            var services = ((App)global::Avalonia.Application.Current!).ServiceProvider!;
            var diagram = new ArmourDiagram(
                services.GetRequiredService<IRecordSheetTemplateProvider>(),
                services.GetRequiredService<IRecordSheetLayout>(),
                services.GetRequiredService<ILogger<ArmourDiagram>>());
            SheetHost.Children.Add(diagram);
            await diagram.RenderAsync(RecordSheetSamples.LightMech);
        }
        catch (Exception ex)
        {
            var logger = ((App)global::Avalonia.Application.Current!).ServiceProvider!
                .GetRequiredService<ILogger<RecordSheetSpikeView>>();
            logger.LogWarning(ex, "Record-sheet spike view failed to load");
        }
    }
}
