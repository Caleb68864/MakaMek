namespace Sanet.MakaMek.Assets.Services;

/// <summary>Loads optional, external BattleMech fluff artwork by its MegaMek unit name.</summary>
public interface IRecordSheetArtworkProvider
{
    Task<Stream?> GetMechArtworkAsync(string mechName);
}
