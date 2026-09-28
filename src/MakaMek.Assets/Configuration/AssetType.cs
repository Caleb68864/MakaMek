namespace Sanet.MakaMek.Assets.Configuration;

/// <summary>
/// The kind of game assets a provider serves.
/// </summary>
public enum AssetType
{
    /// <summary>
    /// BattleMech/unit data.
    /// </summary>
    Units,

    /// <summary>
    /// Hex/terrain data.
    /// </summary>
    Hexes,

    /// <summary>
    /// Blank record sheet templates, as SVG.
    /// </summary>
    RecordSheetTemplates,

    /// <summary>
    /// Armour and structure pip clusters overlaid onto a record sheet, as SVG.
    /// </summary>
    RecordSheetPips,

    /// <summary>
    /// Per-unit fluff artwork shown on a record sheet.
    /// </summary>
    UnitFluff
}
