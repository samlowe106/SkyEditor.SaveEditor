using System;
using System.IO;

namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    /// <summary>Stat gains on reaching one level (one row of a species' growth table).</summary>
    public readonly record struct RBStatGains(int HP, int Attack, int SpAttack, int Defense, int SpDefense);

    /// <summary>Level-1 base stats of a species.</summary>
    public readonly record struct RBBaseStats(int HP, int Attack, int SpAttack, int Defense, int SpDefense);

    /// <summary>
    /// Every species' real growth table (levels 1-100: cumulative EXP required, and the exact
    /// HP/Atk/SpAtk/Def/SpDef gained on reaching each level) plus level-1 base stats, extracted
    /// from the ROM's own <c>lvmp###</c> files and monster data by tools/build_growth_tables.py
    /// into the embedded RBGrowthTables.bin.
    /// </summary>
    /// <remarks>
    /// Stat growth in this game is fully deterministic: no formula, no RNG, just this per-species
    /// table. <c>LevelUp</c> (src/dungeon_leveling.c:403-430) adds row L when reaching level L,
    /// capping HP at 999 and the others at 255; level-down (same file, 480-520) subtracts row L
    /// when leaving level L, flooring everything at 1. <see cref="SetLevel"/> reproduces both
    /// directions so an edited level comes with the stats a legitimately leveled Pokemon would
    /// have. Verified: base + summed gains reproduce all 204 independently-built
    /// <see cref="RBRecruitGuide"/> entries exactly (see RBGrowthTablesTests).
    /// </remarks>
    public static class RBGrowthTables
    {
        public const int SpeciesCount = 421;
        public const int MaxLevel = 100;
        private const int BaseStatsSize = 6;
        private const int RowSize = 10;
        private const int HeaderSize = 8;

        private static readonly byte[] Data = Load();

        private static byte[] Load()
        {
            var assembly = typeof(RBGrowthTables).Assembly;
            using var stream = assembly.GetManifestResourceStream("SkyEditor.SaveEditor.Resources.RBGrowthTables.bin")
                ?? throw new InvalidOperationException("RBGrowthTables.bin embedded resource not found.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (bytes.Length < HeaderSize || bytes[0] != 'R' || bytes[1] != 'B' || bytes[2] != 'G' || bytes[3] != 'T'
                || BitConverter.ToUInt16(bytes, 4) != SpeciesCount || BitConverter.ToUInt16(bytes, 6) != MaxLevel)
            {
                throw new InvalidOperationException("RBGrowthTables.bin has an unexpected header.");
            }
            return bytes;
        }

        private static bool ValidSpecies(int speciesId) => speciesId >= 1 && speciesId <= SpeciesCount;

        private static int RowOffset(int speciesId, int level) =>
            HeaderSize + SpeciesCount * BaseStatsSize + ((speciesId - 1) * MaxLevel + (level - 1)) * RowSize;

        /// <summary>Level-1 base stats, or null for an id without data.</summary>
        public static RBBaseStats? BaseStats(int speciesId)
        {
            if (!ValidSpecies(speciesId)) return null;
            var o = HeaderSize + (speciesId - 1) * BaseStatsSize;
            return new RBBaseStats(BitConverter.ToUInt16(Data, o), Data[o + 2], Data[o + 3], Data[o + 4], Data[o + 5]);
        }

        /// <summary>
        /// Cumulative EXP required to reach <paramref name="level"/> (1-100), or null if the
        /// species/level has no real data (a placeholder species, or -- since real curves never
        /// require 0 EXP past level 1 -- a level past what the species' table covers).
        /// </summary>
        public static uint? ExpRequiredForLevel(int speciesId, int level)
        {
            if (!ValidSpecies(speciesId) || level < 1 || level > MaxLevel) return null;
            var value = BitConverter.ToUInt32(Data, RowOffset(speciesId, level));
            return value == 0 && level > 1 ? null : value;
        }

        /// <summary>The stats gained on reaching <paramref name="level"/> (all zero at level 1), or
        /// null if the species/level has no data.</summary>
        public static RBStatGains? GainsAtLevel(int speciesId, int level)
        {
            if (!ValidSpecies(speciesId) || level < 1 || level > MaxLevel) return null;
            if (level > 1 && ExpRequiredForLevel(speciesId, level) == null) return null;
            var o = RowOffset(speciesId, level) + 4;
            return new RBStatGains(BitConverter.ToUInt16(Data, o), Data[o + 2], Data[o + 3], Data[o + 4], Data[o + 5]);
        }

        /// <summary>The highest level (1-100) whose cumulative EXP requirement is at most
        /// <paramref name="exp"/>, or null if the species has no data.</summary>
        public static int? LevelForExp(int speciesId, int exp)
        {
            if (ExpRequiredForLevel(speciesId, 2) == null) return null;
            var level = 1;
            for (var candidate = 2; candidate <= MaxLevel; candidate++)
            {
                var required = ExpRequiredForLevel(speciesId, candidate);
                if (required == null || required.Value > exp) break;
                level = candidate;
            }
            return level;
        }

        /// <summary>
        /// Changes <paramref name="pokemon"/>'s level the way the game would: leveling up adds
        /// each intermediate level's gains (HP capped at 999, the rest at 255), leveling down
        /// subtracts them (floored at 1), and EXP snaps to the new level's cumulative
        /// requirement unless <paramref name="keepExp"/> is set (for the caller that derived the
        /// level from a typed EXP value). Returns false, changing nothing, if the species has no
        /// growth data; the caller can then just set the level directly.
        /// </summary>
        public static bool SetLevel(RBStoredPokemon pokemon, int newLevel, bool keepExp = false)
        {
            newLevel = Math.Clamp(newLevel, 1, MaxLevel);
            var oldLevel = Math.Clamp(pokemon.Level, 1, MaxLevel);
            if (ExpRequiredForLevel(pokemon.ID, 2) == null) return false;

            int hp = pokemon.HP, atk = pokemon.Attack, spatk = pokemon.SpAttack, def = pokemon.Defense, spdef = pokemon.SpDefense;
            for (var level = oldLevel + 1; level <= newLevel; level++)
            {
                if (GainsAtLevel(pokemon.ID, level) is not { } g) break;
                hp = Math.Min(999, hp + g.HP);
                atk = Math.Min(255, atk + g.Attack);
                spatk = Math.Min(255, spatk + g.SpAttack);
                def = Math.Min(255, def + g.Defense);
                spdef = Math.Min(255, spdef + g.SpDefense);
            }
            for (var level = oldLevel; level > newLevel; level--)
            {
                if (GainsAtLevel(pokemon.ID, level) is not { } g) break;
                hp = Math.Max(1, hp - g.HP);
                atk = Math.Max(1, atk - g.Attack);
                spatk = Math.Max(1, spatk - g.SpAttack);
                def = Math.Max(1, def - g.Defense);
                spdef = Math.Max(1, spdef - g.SpDefense);
            }

            pokemon.HP = hp;
            pokemon.Attack = atk;
            pokemon.SpAttack = spatk;
            pokemon.Defense = def;
            pokemon.SpDefense = spdef;
            pokemon.Level = newLevel;
            if (!keepExp && ExpRequiredForLevel(pokemon.ID, newLevel) is { } exp)
            {
                pokemon.Exp = (int)exp;
            }
            return true;
        }
    }
}
