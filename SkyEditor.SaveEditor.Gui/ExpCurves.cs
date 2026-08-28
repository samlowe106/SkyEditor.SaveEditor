namespace SkyEditor.SaveEditor.Gui;

/// <summary>
/// Real per-species EXP-to-level curves (cumulative EXP required to reach each level 1-100),
/// extracted directly from a ROM dump by tools/build_exp_curves.py -- the same lvmp### growth
/// table extraction pipeline that produces RBRecruitGuideData.generated.cs (spot-checked against
/// that already-verified data: Charmander at level 30 matches its 112290 exactly). This game's
/// growth data is a fixed per-species table, not a shared growth-rate formula (see
/// tools/build_recruit_guide.py's comments), so there's no way to compute this from level/species
/// alone -- it has to be looked up.
/// </summary>
internal static class ExpCurves
{
    private const int SpeciesCount = 421;
    private const int MaxLevel = 100;

    private static readonly uint[] Data = Load();

    private static uint[] Load()
    {
        using var stream = typeof(ExpCurves).Assembly.GetManifestResourceStream("SkyEditor.SaveEditor.Gui.ExpCurves.bin")
            ?? throw new InvalidOperationException("ExpCurves.bin embedded resource not found.");
        using var reader = new BinaryReader(stream);
        var bytes = reader.ReadBytes(SpeciesCount * MaxLevel * 4);
        var values = new uint[SpeciesCount * MaxLevel];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    /// <summary>
    /// Cumulative EXP required for <paramref name="speciesId"/> to reach <paramref name="level"/>
    /// (1-100), or null if that species/level combination has no real data (a non-recruitable
    /// placeholder species, or -- since real curves never require 0 Exp past level 1 -- a level
    /// past what this species' extracted table actually covers).
    /// </summary>
    public static uint? ExpRequiredForLevel(int speciesId, int level)
    {
        if (speciesId < 1 || speciesId > SpeciesCount || level < 1 || level > MaxLevel)
        {
            return null;
        }
        var value = Data[(speciesId - 1) * MaxLevel + (level - 1)];
        return value == 0 && level > 1 ? null : value;
    }
}
