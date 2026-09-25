using Sanet.MakaMek.Core.Models.Units;
using Sanet.MVVM.Core.ViewModels;

namespace Sanet.MakaMek.Presentation.RecordSheet;

/// <summary>Read-only projection of the selected unit's armour and internal structure.</summary>
public sealed class RecordSheetViewModel : BaseViewModel
{
    private IUnit? _unit;
    private RecordSheetDiagramData? _diagramData;

    public IUnit? Unit
    {
        get => _unit;
        private set => SetProperty(ref _unit, value);
    }

    public RecordSheetDiagramData? DiagramData
    {
        get => _diagramData;
        private set => SetProperty(ref _diagramData, value);
    }

    public void SelectUnit(IUnit? unit)
    {
        Unit = unit;
        Refresh();
    }

    /// <summary>Refreshes the projection after the game applies a state-changing command.</summary>
    public void Refresh() => DiagramData = Unit is null ? null : RecordSheetDiagramData.FromUnit(Unit);
}
