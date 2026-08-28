namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    /// <summary>
    /// A friend area (the areas on the map screen where recruited Pokemon live).
    /// </summary>
    /// <remarks>
    /// Mirrors the FRIEND_AREA_* constants in the pret/pmd-red decomp
    /// (include/constants/friend_area.h). <see cref="None"/> (0) is a real bit
    /// position in the save (<see cref="RBSave.FriendAreasUnlocked"/> is
    /// FRIEND_AREA_COUNT = 58 bits long, index 0 included) but is never set by
    /// the game.
    /// </remarks>
    public enum RBFriendArea
    {
        None = 0,
        BountifulSea = 1,
        TreasureSea = 2,
        SereneSea = 3,
        DeepSeaFloor = 4,
        DeepSeaCurrent = 5,
        SeafloorCave = 6,
        ShallowBeach = 7,
        MtDeepgreen = 8,
        MtCleft = 9,
        MtMoonview = 10,
        RainbowPeak = 11,
        WildPlains = 12,
        BeauPlains = 13,
        SkyBluePlains = 14,
        Safari = 15,
        ScorchedPlains = 16,
        SacredField = 17,
        MistRiseForest = 18,
        FlyawayForest = 19,
        OvergrownForest = 20,
        EnergeticForest = 21,
        MushroomForest = 22,
        HealingForest = 23,
        TransformForest = 24,
        SecretiveForest = 25,
        RubADubRiver = 26,
        TadpolePond = 27,
        TurtleshellPond = 28,
        MysticLake = 29,
        WaterfallLake = 30,
        PeanutSwamp = 31,
        PoisonSwamp = 32,
        EchoCave = 33,
        CrypticCave = 34,
        DragonCave = 35,
        BoulderCave = 36,
        Jungle = 37,
        DecrepitLab = 38,
        MtDiscipline = 39,
        ThunderMeadow = 40,
        PowerPlant = 41,
        Crater = 42,
        FurnaceDesert = 43,
        AgedChamberAn = 44,
        AgedChamberOExclaim = 45,
        AncientRelic = 46,
        DarknessRidge = 47,
        FrigidCavern = 48,
        IceFloeBeach = 49,
        VolcanicPit = 50,
        StratosLookout = 51,
        RavagedField = 52,
        MagneticQuarry = 53,
        LegendaryIsland = 54,
        SouthernIsland = 55,
        EnclosedIsland = 56,
        FinalIsland = 57,
    }
}
