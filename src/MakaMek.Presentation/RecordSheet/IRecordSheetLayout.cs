using Sanet.MakaMek.Core.Models.Units;

namespace Sanet.MakaMek.Presentation.RecordSheet;

public enum ArmourFace
{
    Front,
    Rear
}

public interface IRecordSheetLayout
{
    string? TemplateRegionId(PartLocation location, ArmourFace face);

    string? StructureRegionId(PartLocation location);

    string? ArmourClusterName(PartLocation location, ArmourFace face, int value);

    string? StructureClusterName(PartLocation location, int tonnage);
}
