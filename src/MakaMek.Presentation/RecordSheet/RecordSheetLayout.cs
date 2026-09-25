using Sanet.MakaMek.Core.Models.Units;

namespace Sanet.MakaMek.Presentation.RecordSheet;

public sealed class RecordSheetLayout : IRecordSheetLayout
{
    public string? TemplateRegionId(PartLocation location, ArmourFace face)
    {
        if (face == ArmourFace.Rear)
        {
            if (!location.IsTorso()) return null;
            return location switch
            {
                PartLocation.CenterTorso => "armorPipsCTR",
                PartLocation.LeftTorso => "armorPipsLTR",
                PartLocation.RightTorso => "armorPipsRTR",
                _ => null
            };
        }

        return location switch
        {
            PartLocation.Head => "armorPipsHD",
            PartLocation.CenterTorso => "armorPipsCT",
            PartLocation.LeftTorso => "armorPipsLT",
            PartLocation.RightTorso => "armorPipsRT",
            PartLocation.LeftArm => "armorPipsLA",
            PartLocation.RightArm => "armorPipsRA",
            PartLocation.LeftLeg => "armorPipsLL",
            PartLocation.RightLeg => "armorPipsRL",
            _ => null
        };
    }

    public string? ArmourClusterName(PartLocation location, ArmourFace face, int value)
    {
        if (value <= 0 || TemplateRegionId(location, face) is null) return null;
        var code = PartCode(location);
        if (code is null) return null;

        var rearSuffix = face == ArmourFace.Rear ? "_R" : string.Empty;
        return $"Armor_{code}{rearSuffix}_{value}_Humanoid.svg";
    }

    public string? StructureRegionId(PartLocation location)
    {
        var code = PartCode(location);
        return code is null ? null : $"isPips{code}";
    }

    /// <summary>The structure pip library is keyed by chassis tonnage, not structure points.</summary>
    public string? StructureClusterName(PartLocation location, int tonnage)
    {
        var code = PartCode(location);
        return code is null || tonnage <= 0 ? null : $"BipedIS{tonnage}_{code}.svg";
    }

    private static string? PartCode(PartLocation location) => location switch
    {
        PartLocation.Head => "HD",
        PartLocation.CenterTorso => "CT",
        PartLocation.LeftTorso => "LT",
        PartLocation.RightTorso => "RT",
        PartLocation.LeftArm => "LA",
        PartLocation.RightArm => "RA",
        PartLocation.LeftLeg => "LL",
        PartLocation.RightLeg => "RL",
        _ => null
    };
}
