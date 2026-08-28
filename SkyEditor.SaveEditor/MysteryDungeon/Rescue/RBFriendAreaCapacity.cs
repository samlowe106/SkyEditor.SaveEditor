namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    /// <summary>
    /// Per-area roster capacity, mirroring <c>gFriendAreaSettings[].num_pokemon</c>
    /// (src/dungeon_data.c) in <see cref="RBFriendArea"/> order.
    /// </summary>
    /// <remarks>
    /// This is the actual mechanism the game uses to decide "who lives in this friend
    /// area": the entire 413-slot recruited-Pokemon roster (<see cref="RBOffsets.StoredPokemonCount"/>)
    /// is statically partitioned into contiguous slot ranges, one per area, in ascending
    /// <see cref="RBFriendArea"/> order (src/friend_area.c's <c>sub_80923D4</c> and
    /// <c>GetFriendAreaCapacity</c>) -- residency is purely about which range a Pokemon's
    /// roster slot falls in, not its species. Adding a Pokemon to
    /// <see cref="RBSave.StoredPokemon"/> at an arbitrary slot (e.g. just appending it) will
    /// not make it show up as living in the intended area in-game; it needs a slot from
    /// <see cref="SlotRange(RBFriendArea)"/> specifically. The 57 non-<see cref="RBFriendArea.None"/>
    /// capacities below sum to exactly 413, confirming the roster array has no slots outside
    /// any area's range (every recruited Pokemon, including the active leader/partner, sits
    /// in some area's range; <c>GetFriendAreaCapacity</c> just excludes them from that area's
    /// displayed occupant count while they're on the active team).
    /// </remarks>
    public static class RBFriendAreaCapacity
    {
        private static readonly int[] Capacities =
        {
            /* None                 */ 0,
            /* BountifulSea         */ 9,
            /* TreasureSea          */ 10,
            /* SereneSea            */ 4,
            /* DeepSeaFloor         */ 12,
            /* DeepSeaCurrent       */ 1,
            /* SeafloorCave         */ 1,
            /* ShallowBeach         */ 5,
            /* MtDeepgreen          */ 12,
            /* MtCleft              */ 9,
            /* MtMoonview           */ 6,
            /* RainbowPeak          */ 1,
            /* WildPlains           */ 13,
            /* BeauPlains           */ 12,
            /* SkyBluePlains        */ 13,
            /* Safari               */ 15,
            /* ScorchedPlains       */ 10,
            /* SacredField          */ 3,
            /* MistRiseForest       */ 14,
            /* FlyawayForest        */ 12,
            /* OvergrownForest      */ 9,
            /* EnergeticForest      */ 15,
            /* MushroomForest       */ 7,
            /* HealingForest        */ 1,
            /* TransformForest      */ 6,
            /* SecretiveForest      */ 9,
            /* RubADubRiver         */ 7,
            /* TadpolePond          */ 9,
            /* TurtleshellPond      */ 10,
            /* MysticLake           */ 4,
            /* WaterfallLake        */ 4,
            /* PeanutSwamp          */ 7,
            /* PoisonSwamp          */ 6,
            /* EchoCave             */ 11,
            /* CrypticCave          */ 1,
            /* DragonCave           */ 3,
            /* BoulderCave          */ 4,
            /* Jungle               */ 13,
            /* DecrepitLab          */ 7,
            /* MtDiscipline         */ 11,
            /* ThunderMeadow        */ 11,
            /* PowerPlant           */ 6,
            /* Crater               */ 7,
            /* FurnaceDesert        */ 7,
            /* AgedChamberAn        */ 14,
            /* AgedChamberOExclaim  */ 14,
            /* AncientRelic         */ 6,
            /* DarknessRidge        */ 13,
            /* FrigidCavern         */ 7,
            /* IceFloeBeach         */ 5,
            /* VolcanicPit          */ 1,
            /* StratosLookout       */ 1,
            /* RavagedField         */ 5,
            /* MagneticQuarry       */ 3,
            /* LegendaryIsland      */ 3,
            /* SouthernIsland       */ 2,
            /* EnclosedIsland       */ 1,
            /* FinalIsland          */ 1,
        };

        public static int Capacity(RBFriendArea area) => Capacities[(int)area];

        /// <summary>The [start, start+count) roster slot range that belongs to this area.</summary>
        public static (int Start, int Count) SlotRange(RBFriendArea area)
        {
            var start = 0;
            for (var i = 0; i < (int)area; i++)
            {
                start += Capacities[i];
            }
            return (start, Capacities[(int)area]);
        }

        /// <summary>
        /// The friend area whose slot range contains this roster slot, i.e. the inverse of
        /// <see cref="SlotRange(RBFriendArea)"/>. The game decides "which area does this
        /// Pokemon live in" purely by this partition (sub_80923D4, src/friend_area.c).
        /// Returns <see cref="RBFriendArea.None"/> for a slot past every area's range.
        /// </summary>
        public static RBFriendArea AreaForSlot(int slotIndex)
        {
            var start = 0;
            for (var i = 0; i < Capacities.Length; i++)
            {
                start += Capacities[i];
                if (slotIndex < start)
                {
                    return (RBFriendArea)i;
                }
            }
            return RBFriendArea.None;
        }
    }
}
