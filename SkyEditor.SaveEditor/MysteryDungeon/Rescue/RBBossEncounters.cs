using System.Collections.Generic;

namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    /// <summary>
    /// Maps story-boss RB monster IDs to the cutscene "complete" flag (if any)
    /// that must be set, in addition to the Pokemon being present in the
    /// recruited roster, for the game to treat that boss encounter as already
    /// resolved.
    /// </summary>
    /// <remarks>
    /// Determined by inspecting each boss's own dungeon_cutscene_&lt;boss&gt;.c in
    /// the pret/pmd-red decomp: every story boss checked (Zapdos, Moltres,
    /// Articuno, Groudon, Rayquaza, Mewtwo, Entei, Raikou, Suicune, Ho-Oh,
    /// Jirachi, Lugia, Kyogre, Deoxys, Celebi) calls <c>HasRecruitedMon()</c>
    /// directly to decide whether to skip the fight on a revisit. A subset of
    /// those also require a matching <c>CUTSCENE_FLAG_*_COMPLETE</c> bit
    /// (defined in include/constants/cutscenes.h) to avoid replaying the
    /// first-encounter cutscene before the post-story free-roam mode is
    /// unlocked; bosses not listed here (Lugia, Kyogre, Deoxys, Celebi, the
    /// three Regis, and dojo maze bosses) don't have a COMPLETE flag defined
    /// at all and rely on the roster check alone.
    ///
    /// RB monster IDs here match SkyEditor.SaveEditor.Lists.RBPokemon (which
    /// in turn match MONSTER_* constants in the decomp directly, confirmed by
    /// cross-reference, e.g. Zapdos = 145 in both).
    /// </remarks>
    public static class RBBossEncounters
    {
        public const int Articuno = 144;
        public const int Zapdos = 145;
        public const int Moltres = 146;
        public const int Mewtwo = 150;
        public const int Raikou = 268;
        public const int Entei = 269;
        public const int Suicune = 270;
        public const int Lugia = 274;
        public const int HoOh = 275;
        public const int Celebi = 276;
        public const int Regirock = 405;
        public const int Regice = 406;
        public const int Registeel = 407;
        public const int Latias = 408;
        public const int Latios = 409;
        public const int Kyogre = 410;
        public const int Groudon = 411;
        public const int Rayquaza = 412;
        public const int Jirachi = 413;
        public const int Deoxys = 414;

        /// <summary>
        /// Boss RB monster ID -> the <see cref="RBCutsceneFlag"/> that must also
        /// be set for the boss to be considered fully resolved. Bosses without
        /// an entry here only need a recruited-roster entry.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, RBCutsceneFlag> CompleteFlagsByBoss = new Dictionary<int, RBCutsceneFlag>
        {
            [Articuno] = RBCutsceneFlag.FrostyGrottoComplete,
            [Zapdos] = RBCutsceneFlag.MtThunderPeakComplete,
            [Moltres] = RBCutsceneFlag.MtBlazePeakComplete,
            [Mewtwo] = RBCutsceneFlag.WesternCaveComplete,
            [Raikou] = RBCutsceneFlag.LightningFieldComplete,
            [Entei] = RBCutsceneFlag.FieryFieldComplete,
            [Suicune] = RBCutsceneFlag.NorthwindFieldComplete,
            [HoOh] = RBCutsceneFlag.MtFarawayComplete,
            [Latios] = RBCutsceneFlag.NorthernRangeComplete,
            [Groudon] = RBCutsceneFlag.MagmaCavernPitComplete,
            [Rayquaza] = RBCutsceneFlag.SkyTowerSummitComplete,
            [Jirachi] = RBCutsceneFlag.JirachiComplete,
            // Lugia, Kyogre, Deoxys, Celebi, and the three Regis intentionally
            // have no entry: their cutscene scripts only check HasRecruitedMon.
        };

        /// <summary>
        /// Boss RB monster ID -> the <see cref="RBCutsceneFlag"/> that must already be set before
        /// this species can be recruited at all. Determined by reading
        /// <c>IsMonsterRecruitable</c>/<c>MonCutsceneCompleted</c> in
        /// src/dungeon_mon_recruit.c and src/dungeon_cutscene.c in the pret/pmd-red decomp:
        /// <c>IsMonsterRecruitable</c> hard-fails for these species until their listed flag is
        /// set, and that flag is only ever set by their own boss-faint handler -- so the very
        /// first encounter can end in the boss fainting (no recruit) or fleeing, never a
        /// successful recruit. Only a second, later encounter (after the flag is set) can
        /// actually recruit them.
        /// </summary>
        /// <remarks>
        /// The three Regis are intentionally NOT here even though they're also gated by
        /// <c>MonCutsceneCompleted</c> via <see cref="RBCutsceneFlag.RegiItemObtained"/>: per
        /// src/dungeon_cutscene_regis.c, that flag is recomputed from scratch every time ANY Regi's
        /// room is entered (from whether the player currently holds that specific Regi's Part or
        /// the Music Box), not a persistent "have you ever obtained it" bit -- reading it from a
        /// save file only reflects whatever the last Regi-room visit happened to compute, which has
        /// no reliable bearing on current recruitability. Use <see cref="RBSave.CanCurrentlyRecruit"/>
        /// (which checks current item possession via <see cref="RegiItems"/> instead) for the
        /// Regis. Latios and Latias are excluded here even though they're
        /// listed bosses: <c>IsMonsterRecruitable</c> unconditionally excludes them from combat
        /// recruiting altogether (per the decomp and per SkyEditor.SaveEditor/guide.md, each is
        /// obtained through its own separate scripted story event, not a fight). Jirachi, Lugia, Kyogre,
        /// Deoxys, and Celebi are excluded because they simply aren't referenced in
        /// <c>MonCutsceneCompleted</c>'s switch at all -- nothing in the decomp gates their first
        /// encounter this way.
        /// </remarks>
        public static readonly IReadOnlyDictionary<int, RBCutsceneFlag> FirstEncounterFlagsByBoss = new Dictionary<int, RBCutsceneFlag>
        {
            [Articuno] = RBCutsceneFlag.FrostyGrottoComplete,
            [Zapdos] = RBCutsceneFlag.MtThunderPeakComplete,
            [Moltres] = RBCutsceneFlag.MtBlazePeakComplete,
            [Mewtwo] = RBCutsceneFlag.WesternCaveComplete,
            [Raikou] = RBCutsceneFlag.LightningFieldComplete,
            [Entei] = RBCutsceneFlag.FieryFieldComplete,
            [Suicune] = RBCutsceneFlag.NorthwindFieldComplete,
            [HoOh] = RBCutsceneFlag.MtFarawayComplete,
            [Groudon] = RBCutsceneFlag.MagmaCavernPitComplete,
            [Rayquaza] = RBCutsceneFlag.SkyTowerSummitComplete,
        };

        /// <summary>
        /// Boss RB monster IDs <c>IsMonsterRecruitable</c> unconditionally excludes from combat
        /// recruiting -- not "not yet," but never, at any point in the game, regardless of story
        /// progress or any flag/item state. Latios and Latias are each obtained only through their
        /// own separate scripted story event, not a fight this tool models; recruiting either
        /// through this tool would never correspond to anything legitimately reachable in-game.
        /// </summary>
        public static readonly HashSet<int> NeverCombatRecruitable = new() { Latios, Latias };

        /// <summary>
        /// Item IDs relevant to Regi recruiting, kept in their own nested class rather than as
        /// top-level fields on <see cref="RBBossEncounters"/> -- both this GUI and CLI enumerate
        /// "every boss monster ID" by reflecting over <see cref="RBBossEncounters"/>'s own public
        /// static int fields (see <c>BossSpecies.Ids</c> in the Gui project), so a plain item-ID
        /// constant declared directly on this class would get misread as a 20th boss species.
        /// </summary>
        public static class RegiItems
        {
            /// <summary>
            /// Regi RB monster ID -> the RB item ID of that Regi's "Part" (Rock/Ice/Steel Part),
            /// dropped the first time it's fought and required (or the assembled Music Box, see
            /// <see cref="MusicBoxItemId"/>) to actually recruit it on a later visit. Item IDs
            /// match SkyEditor.SaveEditor's RBItems resource and the decomp's
            /// ITEM_ROCK_PART=121/ITEM_ICE_PART=119/ITEM_STEEL_PART=120 (cross-checked directly
            /// against include/constants/item.h).
            /// </summary>
            public static readonly IReadOnlyDictionary<int, int> PartIdsBySpecies = new Dictionary<int, int>
            {
                [Regirock] = 121, // Rock Part
                [Regice] = 119,   // Ice Part
                [Registeel] = 120, // Steel Part
            };

            /// <summary>RB item ID of the Music Box, assembled from all three Regi Parts -- holding
            /// it substitutes for any single Regi's own Part (see <see cref="PartIdsBySpecies"/>).</summary>
            public const int MusicBoxItemId = 122;
        }
    }
}
