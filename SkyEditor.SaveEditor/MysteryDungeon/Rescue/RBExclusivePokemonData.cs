using System.Collections.Generic;

namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    /// <summary>
    /// One story-progress cutscene flag for a Red/Blue Rescue Team boss encounter.
    /// </summary>
    /// <remarks>
    /// Bit positions mirror <c>enum CutsceneFlagID</c> in the pret/pmd-red decomp
    /// (include/constants/cutscenes.h). Only bit positions with a known, named
    /// meaning are exposed here; the remaining bits up to <see cref="RBExclusivePokemonData.CutsceneFlagCount"/>
    /// are reserved/unused by the game and are preserved as-is on read/write.
    /// </remarks>
    public enum RBCutsceneFlag
    {
        MtSteelReached = 0,
        MtSteelComplete = 1,
        SinisterWoodsReached = 2,
        SinisterWoodsComplete = 3,
        MtThunderPeakReached = 4,
        MtThunderPeakComplete = 5,
        MtBlazePeakReached = 6,
        MtBlazePeakComplete = 7,
        FrostyGrottoReached = 8,
        FrostyGrottoComplete = 9,
        MtFreezePeakComplete = 10,
        MagmaCavernPitReached = 11,
        MagmaCavernPitComplete = 12,
        MagmaCavernMidReached = 13,
        SkyTowerSummitReached = 14,
        SkyTowerSummitComplete = 15,
        UproarForestReached = 16,
        UproarForestComplete = 17,
        WesternCaveReached = 18,
        WesternCaveComplete = 19,
        FieryFieldReached = 20,
        FieryFieldComplete = 21,
        LightningFieldReached = 22,
        LightningFieldComplete = 23,
        NorthwindFieldReached = 24,
        NorthwindFieldComplete = 25,
        MtFarawayComplete = 26,
        NorthernRangeReached = 27,
        NorthernRangeComplete = 28,
        RegiItemObtained = 29,
        JirachiComplete = 30,
        FrostyForestIntruded = 31,
        MedichamComplete = 32,
        HowlingForestComplete = 33,
        RegiRecruited = 34,
    }

    /// <summary>
    /// Story-progress data for Red/Blue Rescue Team: which Pokemon have been seen,
    /// which boss-encounter cutscene flags have been set, tutorial popups shown,
    /// and which "exclusive" (one-per-file) Pokemon have been claimed.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>ExclusivePokemonData</c> and <c>WriteExclusivePokemon</c>/<c>ReadExclusivePokemon</c>
    /// (src/exclusive_pokemon.c) in the pret/pmd-red decomp. Field order and bit
    /// widths must match the decomp exactly since they're serialized as a single
    /// contiguous bitstream with no padding between fields.
    ///
    /// Wired into <see cref="RBSave"/> at <see cref="RBSave.RBOffsets.ExclusivePokemonDataOffset"/>
    /// and empirically verified against a real save file: every species currently in
    /// <see cref="RBSave.StoredPokemon"/> also reads true from <see cref="RBSave.HasRecruitedSpeciesFlag"/>
    /// (a 424-bit bitmap earlier in the same contiguous AdventureBits/ExclusivePokemonData
    /// bitstream) -- a coincidental match across 20 arbitrary species IDs scattered over 424 bit
    /// positions would be astronomically unlikely if this offset were wrong. See
    /// <see cref="RBSave.RBOffsets.AdventureBitsBitLength"/>'s remarks for the matching
    /// numJoined-against-the-in-game-Adventure-Log check that anchors the offset immediately
    /// before this one.
    /// </remarks>
    public class RBExclusivePokemonData
    {
        /// <summary>Highest valid monster ID + 1 (MONSTER_MAX in the decomp).</summary>
        public const int MonsterSeenFlagCount = 424;

        /// <summary>NUM_CUTSCENE_FLAGS in the decomp.</summary>
        public const int CutsceneFlagCount = 64;

        /// <summary>NUM_TUTORIAL_FLAGS in the decomp.</summary>
        public const int TutorialFlagCount = 31;

        /// <summary>NUM_EXCLUSIVE_POKEMON in the decomp.</summary>
        public const int ExclusivePokemonCount = 12;

        public const int BitLength = 1 + MonsterSeenFlagCount + CutsceneFlagCount + TutorialFlagCount + ExclusivePokemonCount;

        public RBExclusivePokemonData()
        {
            Unknown0 = false;
            MonsterSeenFlags = new bool[MonsterSeenFlagCount];
            CutsceneFlags = new bool[CutsceneFlagCount];
            TutorialFlags = new bool[TutorialFlagCount];
            ExclusivePokemonClaimed = new bool[ExclusivePokemonCount];
        }

        public RBExclusivePokemonData(BitBlock bits) : this()
        {
            Initialize(bits);
        }

        /// <summary>Unknown single-bit field written before everything else.</summary>
        public bool Unknown0 { get; set; }

        /// <summary>Pokedex "seen" flags, indexed by RB monster ID.</summary>
        public bool[] MonsterSeenFlags { get; }

        /// <summary>Raw cutscene flag bits, indexed by <see cref="RBCutsceneFlag"/> (or raw position for unnamed bits).</summary>
        public bool[] CutsceneFlags { get; }

        public bool[] TutorialFlags { get; }

        /// <summary>Whether each of the 12 "exclusive" (one-per-save) Pokemon has been claimed.</summary>
        public bool[] ExclusivePokemonClaimed { get; }

        public bool GetCutsceneFlag(RBCutsceneFlag flag) => CutsceneFlags[(int)flag];

        public void SetCutsceneFlag(RBCutsceneFlag flag, bool value) => CutsceneFlags[(int)flag] = value;

        /// <summary>
        /// Sets the cutscene "complete" flag for the given boss's story
        /// encounter, if that boss has one (see <see cref="RBBossEncounters"/>).
        /// </summary>
        /// <param name="bossMonsterId">RB monster ID of the boss, e.g. <see cref="RBBossEncounters.Zapdos"/>.</param>
        /// <returns>True if a flag was set; false if this boss has no complete flag (roster presence alone is sufficient for it).</returns>
        /// <remarks>
        /// Deliberately does nothing special for a Regi: <see cref="RBCutsceneFlag.RegiRecruited"/>
        /// is transient scratch state the game recomputes from <c>HasRecruitedMon</c> every time any
        /// Regi's room is entered (see dungeon_cutscene_regis.c), so writing it here wouldn't
        /// persist any real signal -- the roster entry alone is the actual, lasting source of truth
        /// for a Regi. See <see cref="RBSave.CanCurrentlyRecruit"/> for whether a Regi can currently
        /// be recruited (an item-possession check, not a flag).
        /// </remarks>
        public bool MarkBossDefeated(int bossMonsterId)
        {
            if (RBBossEncounters.CompleteFlagsByBoss.TryGetValue(bossMonsterId, out var flag))
            {
                SetCutsceneFlag(flag, true);
                return true;
            }
            return false;
        }

        /// <summary>
        /// False only for a species in <see cref="RBBossEncounters.FirstEncounterFlagsByBoss"/>
        /// whose listed flag isn't set yet -- meaning the real game would never let this species be
        /// recruited yet, since its mandatory first story encounter (fight, no recruit possible)
        /// hasn't happened. True for every other species, including every boss not in that
        /// dictionary, since nothing in the decomp gates their first encounter this way. Always
        /// true for the three Regis specifically since they're deliberately absent from that
        /// dictionary -- use <see cref="RBSave.CanCurrentlyRecruit"/> for them instead, which checks
        /// item possession rather than a cutscene flag.
        /// </summary>
        public bool HasCompletedFirstEncounter(int speciesId) =>
            !RBBossEncounters.FirstEncounterFlagsByBoss.TryGetValue(speciesId, out var flag) || GetCutsceneFlag(flag);

        private void Initialize(BitBlock bits)
        {
            var position = 0;

            Unknown0 = bits[position];
            position += 1;

            for (int i = 0; i < MonsterSeenFlagCount; i++)
            {
                MonsterSeenFlags[i] = bits[position + i];
            }
            position += MonsterSeenFlagCount;

            for (int i = 0; i < CutsceneFlagCount; i++)
            {
                CutsceneFlags[i] = bits[position + i];
            }
            position += CutsceneFlagCount;

            for (int i = 0; i < TutorialFlagCount; i++)
            {
                TutorialFlags[i] = bits[position + i];
            }
            position += TutorialFlagCount;

            for (int i = 0; i < ExclusivePokemonCount; i++)
            {
                ExclusivePokemonClaimed[i] = bits[position + i];
            }
        }

        public BitBlock ToBitBlock()
        {
            var bits = new BitBlock(BitLength);
            var position = 0;

            bits[position] = Unknown0;
            position += 1;

            for (int i = 0; i < MonsterSeenFlagCount; i++)
            {
                bits[position + i] = MonsterSeenFlags[i];
            }
            position += MonsterSeenFlagCount;

            for (int i = 0; i < CutsceneFlagCount; i++)
            {
                bits[position + i] = CutsceneFlags[i];
            }
            position += CutsceneFlagCount;

            for (int i = 0; i < TutorialFlagCount; i++)
            {
                bits[position + i] = TutorialFlags[i];
            }
            position += TutorialFlagCount;

            for (int i = 0; i < ExclusivePokemonCount; i++)
            {
                bits[position + i] = ExclusivePokemonClaimed[i];
            }

            return bits;
        }
    }
}
