using System.Collections.ObjectModel;
using Sanet.MakaMek.Core.Models.Units;
using Sanet.MakaMek.Core.Models.Units.Mechs;

namespace Sanet.MakaMek.Presentation.RecordSheet;

public readonly record struct ArmourRegion(PartLocation Location, ArmourFace Face);

public sealed record RecordSheetPartData(
    int CurrentArmor,
    int MaxArmor,
    int CurrentRearArmor,
    int MaxRearArmor,
    int CurrentStructure,
    int MaxStructure,
    bool IsDestroyed,
    bool IsBlownOff);

public enum CriticalSlotState
{
    Empty,
    Intact,
    Hit,
    Destroyed
}

public sealed record RecordSheetCriticalSlotData(
    int Slot,
    string? ComponentName,
    CriticalSlotState State);

/// <summary>Static, already-resolved values used by the slice-2 visual spike.</summary>
public sealed record RecordSheetDiagramData(
    int Tonnage,
    IReadOnlyDictionary<ArmourRegion, int> Armour,
    IReadOnlyDictionary<PartLocation, RecordSheetPartData>? PartStates = null,
    IReadOnlySet<PartLocation>? RecentDamageLocations = null,
    IReadOnlyDictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>>? CriticalSlotsByLocation = null,
    string? FluffArtworkName = null)
{
    public IReadOnlyDictionary<PartLocation, RecordSheetPartData> Locations =>
        PartStates ?? EmptyPartStates;

    public IReadOnlySet<PartLocation> RecentlyDamagedLocations =>
        RecentDamageLocations ?? EmptyRecentDamageLocations;

    public IReadOnlyDictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>> CriticalSlots =>
        CriticalSlotsByLocation ?? EmptyCriticalSlots;

    private static IReadOnlyDictionary<PartLocation, RecordSheetPartData> EmptyPartStates { get; } =
        new ReadOnlyDictionary<PartLocation, RecordSheetPartData>(
            new Dictionary<PartLocation, RecordSheetPartData>());

    private static IReadOnlySet<PartLocation> EmptyRecentDamageLocations { get; } = new HashSet<PartLocation>();

    private static IReadOnlyDictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>> EmptyCriticalSlots { get; } =
        new ReadOnlyDictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>>(
            new Dictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>>());

    public static RecordSheetDiagramData Create(
        int tonnage,
        IEnumerable<KeyValuePair<ArmourRegion, int>> armour) =>
        new(tonnage, new ReadOnlyDictionary<ArmourRegion, int>(new Dictionary<ArmourRegion, int>(armour)));

    public static RecordSheetDiagramData FromUnit(IUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var armour = new Dictionary<ArmourRegion, int>();
        var states = new Dictionary<PartLocation, RecordSheetPartData>();
        var criticalSlots = new Dictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>>();

        foreach (var (location, part) in unit.Parts)
        {
            var isBlownOff = part.IsBlownOff;
            armour[new ArmourRegion(location, ArmourFace.Front)] = isBlownOff ? 0 : part.CurrentArmor;
            if (part is Torso torso)
                armour[new ArmourRegion(location, ArmourFace.Rear)] = isBlownOff ? 0 : torso.CurrentRearArmor;

            states[location] = new RecordSheetPartData(
                isBlownOff ? 0 : part.CurrentArmor,
                part.MaxArmor,
                isBlownOff || part is not Torso rearTorso ? 0 : rearTorso.CurrentRearArmor,
                part is Torso maxRearTorso ? maxRearTorso.MaxRearArmor : 0,
                isBlownOff ? 0 : part.CurrentStructure,
                part.MaxStructure,
                part.IsDestroyed,
                isBlownOff);

            criticalSlots[location] = Enumerable.Range(0, part.TotalSlots)
                .Select(slot =>
                {
                    var component = part.GetComponentAtSlot(slot);
                    var state = component is null
                        ? CriticalSlotState.Empty
                        : component.IsDestroyed
                            ? CriticalSlotState.Destroyed
                            : part.HitSlots.Contains(slot)
                                ? CriticalSlotState.Hit
                                : CriticalSlotState.Intact;
                    return new RecordSheetCriticalSlotData(slot, component?.Name, state);
                })
                .ToArray();
        }

        return new RecordSheetDiagramData(
            unit.Tonnage,
            new ReadOnlyDictionary<ArmourRegion, int>(armour),
            new ReadOnlyDictionary<PartLocation, RecordSheetPartData>(states),
            CriticalSlotsByLocation: new ReadOnlyDictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>>(criticalSlots),
            FluffArtworkName: unit is Mech mech ? $"{mech.Chassis} {mech.Model}" : null);
    }
}
