using Sanet.MakaMek.Core.Models.Units;
using Sanet.MVVM.Core.ViewModels;

namespace Sanet.MakaMek.Presentation.RecordSheet;

/// <summary>Read-only projection of the selected unit's armour and internal structure.</summary>
public sealed class RecordSheetViewModel : BaseViewModel
{
    private IUnit? _unit;
    private RecordSheetDiagramData? _diagramData;
    private readonly HashSet<PartLocation> _recentDamageLocations = [];

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
        if (!ReferenceEquals(Unit, unit))
            _recentDamageLocations.Clear();
        Unit = unit;
        Refresh();
    }

    /// <summary>Refreshes the projection after the game applies a state-changing command.</summary>
    public void AddRecentDamage(IEnumerable<PartLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        var changed = false;
        foreach (var location in locations)
            changed |= _recentDamageLocations.Add(location);
        if (!changed) return;
        Refresh();
    }

    public void SetRecentDamage(IEnumerable<PartLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        var replacement = locations.ToHashSet();
        if (_recentDamageLocations.SetEquals(replacement)) return;
        _recentDamageLocations.Clear();
        _recentDamageLocations.UnionWith(replacement);
        Refresh();
    }

    public void ClearRecentDamage()
    {
        if (_recentDamageLocations.Count == 0) return;
        _recentDamageLocations.Clear();
        Refresh();
    }

    public void Refresh() => DiagramData = Unit is null
        ? null
        : RecordSheetDiagramData.FromUnit(Unit) with
        {
            RecentDamageLocations = new HashSet<PartLocation>(_recentDamageLocations)
        };
}
