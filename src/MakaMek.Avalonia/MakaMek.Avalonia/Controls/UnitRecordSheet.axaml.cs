using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanet.MakaMek.Assets.Services;
using Sanet.MakaMek.Core.Models.Units;
using Sanet.MakaMek.Core.Models.Units.Mechs;
using Sanet.MakaMek.Presentation.RecordSheet;
using Sanet.MakaMek.Presentation.ViewModels.Wrappers;

namespace Sanet.MakaMek.Avalonia.Controls;

public partial class UnitRecordSheet : UserControl
{
    public static readonly StyledProperty<Unit?> UnitProperty =
        AvaloniaProperty.Register<UnitRecordSheet, Unit?>(nameof(Unit));

    public static readonly StyledProperty<bool> HasPilotProperty =
        AvaloniaProperty.Register<UnitRecordSheet, bool>(nameof(HasPilot));

    static UnitRecordSheet()
    {
        UnitProperty.Changed.AddClassHandler<UnitRecordSheet>((sender, _) => sender.OnUnitChanged());
        DiagramViewModelProperty.Changed.AddClassHandler<UnitRecordSheet>((sender, _) => sender.OnDiagramViewModelChanged());
    }

    public static readonly StyledProperty<bool> ShowHeatLevelPanelProperty =
        AvaloniaProperty.Register<UnitRecordSheet, bool>(nameof(ShowHeatLevelPanel));

    public static readonly StyledProperty<bool> ShowEventsTabProperty =
        AvaloniaProperty.Register<UnitRecordSheet, bool>(nameof(ShowEventsTab));

    public static readonly StyledProperty<object?> HeatProjectionProperty =
        AvaloniaProperty.Register<UnitRecordSheet, object?>(nameof(HeatProjection));

    public static readonly StyledProperty<PilotViewModel?> EditablePilotProperty =
        AvaloniaProperty.Register<UnitRecordSheet, PilotViewModel?>(nameof(EditablePilot));

    public static readonly StyledProperty<bool> CanEditProperty =
        AvaloniaProperty.Register<UnitRecordSheet, bool>(nameof(CanEdit));

    public static readonly StyledProperty<ICommand?> SaveCommandProperty =
        AvaloniaProperty.Register<UnitRecordSheet, ICommand?>(nameof(SaveCommand));

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<UnitRecordSheet, ICommand?>(nameof(CancelCommand));

    public static readonly StyledProperty<string?> EditableNameProperty =
        AvaloniaProperty.Register<UnitRecordSheet, string?>(nameof(EditableName));

    public static readonly StyledProperty<RecordSheetViewModel?> DiagramViewModelProperty =
        AvaloniaProperty.Register<UnitRecordSheet, RecordSheetViewModel?>(nameof(DiagramViewModel));

    public static readonly StyledProperty<bool> PreferDiagramProperty =
        AvaloniaProperty.Register<UnitRecordSheet, bool>(nameof(PreferDiagram));

    public bool PreferDiagram
    {
        get => GetValue(PreferDiagramProperty);
        set => SetValue(PreferDiagramProperty, value);
    }

    private readonly IRecordSheetTemplateProvider? _recordSheetAssets;
    private readonly IRecordSheetLayout? _recordSheetLayout;
    private readonly IRecordSheetArtworkProvider? _recordSheetArtworkProvider;
    private readonly ILogger<ArmourDiagram>? _diagramLogger;
    private RecordSheetViewModel? _localDiagramViewModel;
    private RecordSheetViewModel? _observedDiagramViewModel;
    private ArmourDiagram? _armourDiagram;

    public Unit? Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public bool HasPilot
    {
        get => GetValue(HasPilotProperty);
        set => SetValue(HasPilotProperty, value);
    }

    public bool ShowHeatLevelPanel
    {
        get => GetValue(ShowHeatLevelPanelProperty);
        set => SetValue(ShowHeatLevelPanelProperty, value);
    }

    public bool ShowEventsTab
    {
        get => GetValue(ShowEventsTabProperty);
        set => SetValue(ShowEventsTabProperty, value);
    }

    public object? HeatProjection
    {
        get => GetValue(HeatProjectionProperty);
        set => SetValue(HeatProjectionProperty, value);
    }

    public PilotViewModel? EditablePilot
    {
        get => GetValue(EditablePilotProperty);
        set => SetValue(EditablePilotProperty, value);
    }

    public bool CanEdit
    {
        get => GetValue(CanEditProperty);
        set => SetValue(CanEditProperty, value);
    }

    public ICommand? SaveCommand
    {
        get => GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public string? EditableName
    {
        get => GetValue(EditableNameProperty);
        set => SetValue(EditableNameProperty, value);
    }

    public RecordSheetViewModel? DiagramViewModel
    {
        get => GetValue(DiagramViewModelProperty);
        set => SetValue(DiagramViewModelProperty, value);
    }

    public UnitRecordSheet()
    {
        if (Application.Current is App app && app.ServiceProvider is { } services)
        {
            _recordSheetAssets = services.GetService<IRecordSheetTemplateProvider>();
            _recordSheetLayout = services.GetService<IRecordSheetLayout>();
            _recordSheetArtworkProvider = services.GetService<IRecordSheetArtworkProvider>();
            _diagramLogger = services.GetService<ILogger<ArmourDiagram>>();
        }

        InitializeComponent();
    }

    public UnitRecordSheet(
        IRecordSheetTemplateProvider? recordSheetAssets,
        IRecordSheetLayout? recordSheetLayout,
        ILogger<ArmourDiagram>? diagramLogger)
        : this(recordSheetAssets, recordSheetLayout, diagramLogger, null)
    {
    }

    public UnitRecordSheet(
        IRecordSheetTemplateProvider? recordSheetAssets,
        IRecordSheetLayout? recordSheetLayout,
        ILogger<ArmourDiagram>? diagramLogger,
        IRecordSheetArtworkProvider? recordSheetArtworkProvider)
    {
        _recordSheetAssets = recordSheetAssets;
        _recordSheetLayout = recordSheetLayout;
        _diagramLogger = diagramLogger;
        _recordSheetArtworkProvider = recordSheetArtworkProvider;
        InitializeComponent();
    }

    private void OnUnitChanged()
    {
        SetDiagramAvailability(false);
        if (_armourDiagram is not null)
            _armourDiagram.ViewModel = null;

        var pilot = Unit?.Pilot;
        HasPilot = pilot is not null;
        if (pilot is not null)
        {
            if (EditablePilot?.Pilot != pilot)
            {
                EditablePilot = new PilotViewModel(pilot);
            }
        }
        else
        {
            EditablePilot = null;
        }

        UpdateRecordSheetDiagram();
    }

    private void OnDiagramViewModelChanged()
    {
        if (!ReferenceEquals(_observedDiagramViewModel, DiagramViewModel))
        {
            if (_observedDiagramViewModel is not null)
                _observedDiagramViewModel.PropertyChanged -= OnDiagramViewModelPropertyChanged;

            _observedDiagramViewModel = DiagramViewModel;
            if (_observedDiagramViewModel is not null)
                _observedDiagramViewModel.PropertyChanged += OnDiagramViewModelPropertyChanged;
        }

        UpdateRecordSheetDiagram();
    }

    private void OnDiagramViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecordSheetViewModel.Unit) or nameof(RecordSheetViewModel.DiagramData))
            UpdateRecordSheetDiagram();
    }

    private void UpdateRecordSheetDiagram()
    {
        if (Unit is not Mech mech)
        {
            SetDiagramAvailability(false);
            if (_armourDiagram is not null)
                _armourDiagram.ViewModel = null;
            return;
        }

        if (_recordSheetAssets is null || _recordSheetLayout is null || _diagramLogger is null)
        {
            SetDiagramAvailability(false);
            return;
        }

        var diagramViewModel = DiagramViewModel;
        if (diagramViewModel is null)
        {
            _localDiagramViewModel ??= new RecordSheetViewModel();
            if (!ReferenceEquals(_localDiagramViewModel.Unit, mech))
                _localDiagramViewModel.SelectUnit(mech);
            diagramViewModel = _localDiagramViewModel;
        }

        if (!ReferenceEquals(diagramViewModel.Unit, mech) || diagramViewModel.DiagramData is null)
        {
            SetDiagramAvailability(false);
            if (_armourDiagram is not null)
                _armourDiagram.ViewModel = null;
            return;
        }

        _armourDiagram ??= CreateArmourDiagram();
        _armourDiagram.ViewModel = diagramViewModel;
    }

    private ArmourDiagram CreateArmourDiagram()
    {
        var diagram = new ArmourDiagram(
            _recordSheetAssets!, _recordSheetLayout!, _diagramLogger!, _recordSheetArtworkProvider);
        diagram.TemplateAvailabilityChanged += OnTemplateAvailabilityChanged;
        RecordSheetDiagramHost.Children.Add(diagram);
        return diagram;
    }

    private void OnTemplateAvailabilityChanged(object? sender, bool isAvailable) =>
        SetDiagramAvailability(Unit is Mech && isAvailable);

    private void SetDiagramAvailability(bool isAvailable)
    {
        if (!isAvailable && ReferenceEquals(RecordSheetTabs.SelectedItem, RecordSheetTab))
            RecordSheetTabs.SelectedIndex = 0;
        RecordSheetTab.IsVisible = isAvailable;
        if (isAvailable && PreferDiagram)
            RecordSheetTabs.SelectedItem = RecordSheetTab;
    }
}
