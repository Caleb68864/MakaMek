using Sanet.MakaMek.Core.Models.Units;
using Sanet.MakaMek.Presentation.RecordSheet;

namespace MakaMek.Avalonia.Tests.Controls;

/// <summary>
/// Armour values transcribed from the named MegaMek MTF examples, used as fixture data. Lives in
/// the test projects rather than in the shipped assembly, which has no use for them.
/// </summary>
internal static class RecordSheetSamples
{
    public static RecordSheetDiagramData LightMech { get; } = RecordSheetDiagramData.Create(20,
    [
        Front(PartLocation.Head, 8), Front(PartLocation.CenterTorso, 10),
        Rear(PartLocation.CenterTorso, 2), Front(PartLocation.LeftTorso, 8),
        Rear(PartLocation.LeftTorso, 2), Front(PartLocation.RightTorso, 8),
        Rear(PartLocation.RightTorso, 2), Front(PartLocation.LeftArm, 4),
        Front(PartLocation.RightArm, 4), Front(PartLocation.LeftLeg, 8),
        Front(PartLocation.RightLeg, 8)
    ]);

    public static RecordSheetDiagramData AssaultMech { get; } = RecordSheetDiagramData.Create(100,
    [
        Front(PartLocation.Head, 9), Front(PartLocation.CenterTorso, 47),
        Rear(PartLocation.CenterTorso, 14), Front(PartLocation.LeftTorso, 32),
        Rear(PartLocation.LeftTorso, 10), Front(PartLocation.RightTorso, 32),
        Rear(PartLocation.RightTorso, 10), Front(PartLocation.LeftArm, 34),
        Front(PartLocation.RightArm, 34), Front(PartLocation.LeftLeg, 41),
        Front(PartLocation.RightLeg, 41)
    ]);

    private static KeyValuePair<ArmourRegion, int> Front(PartLocation location, int value) =>
        new(new ArmourRegion(location, ArmourFace.Front), value);

    private static KeyValuePair<ArmourRegion, int> Rear(PartLocation location, int value) =>
        new(new ArmourRegion(location, ArmourFace.Rear), value);
}
