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

/// <summary>Static, already-resolved values used by the slice-2 visual spike.</summary>
public sealed record RecordSheetDiagramData(
    int Tonnage,
    IReadOnlyDictionary<ArmourRegion, int> Armour,
    IReadOnlyDictionary<PartLocation, RecordSheetPartData>? PartStates = null)
{
    public IReadOnlyDictionary<PartLocation, RecordSheetPartData> Locations =>
        PartStates ?? EmptyPartStates;

    private static IReadOnlyDictionary<PartLocation, RecordSheetPartData> EmptyPartStates { get; } =
        new ReadOnlyDictionary<PartLocation, RecordSheetPartData>(
            new Dictionary<PartLocation, RecordSheetPartData>());

    public static RecordSheetDiagramData Create(
        int tonnage,
        IEnumerable<KeyValuePair<ArmourRegion, int>> armour) =>
        new(tonnage, new ReadOnlyDictionary<ArmourRegion, int>(new Dictionary<ArmourRegion, int>(armour)));

    public static RecordSheetDiagramData FromUnit(IUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var armour = new Dictionary<ArmourRegion, int>();
        var states = new Dictionary<PartLocation, RecordSheetPartData>();

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
        }

        return new RecordSheetDiagramData(
            unit.Tonnage,
            new ReadOnlyDictionary<ArmourRegion, int>(armour),
            new ReadOnlyDictionary<PartLocation, RecordSheetPartData>(states));
    }
}
