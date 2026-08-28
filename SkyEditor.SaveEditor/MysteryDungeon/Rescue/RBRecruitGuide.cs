using System.Collections.Generic;
using System.Linq;

namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    /// <summary>
    /// One species' easiest real recruit spot and the exact stats a legitimately recruited
    /// individual of that species would have there, immediately after joining (i.e. before any
    /// further leveling).
    /// </summary>
    /// <remarks>
    /// Sourced from guide.md's recruit-em-all table (species, easiest recruit location, and
    /// recruit level), with the HP/Attack/SpAttack/Defense/SpDefense/Exp columns replaced by
    /// real numbers: each species' <c>lvmp###</c> growth table (a per-species, per-level table
    /// of stat gains, stored as an AT-compressed resource inside the ROM's system file archive)
    /// was extracted directly from the ROM and summed on top of its level-1 base stats
    /// (<c>monster_data.json</c>) from level 2 up to the recruit level -- exactly what
    /// <c>LevelUp()</c> (src/dungeon_leveling.c) does in-game. See TODO.md for how the archive
    /// (<c>data/system_sbin.s</c>'s <c>gSystemFileArchive</c>, a literal <c>incbin baserom.gba</c>
    /// slice) and the AT-decompression format (<c>src/decompress_at.c</c>) were used to do this.
    /// </remarks>
    public sealed record RecruitGuideEntry(
        int SpeciesId,
        string SpeciesName,
        RBFriendArea FriendArea,
        int DungeonId,
        string DungeonName,
        int Floor,
        int Level,
        int HP,
        int Attack,
        int SpAttack,
        int Defense,
        int SpDefense,
        int Exp);

    /// <summary>
    /// Lookup over <see cref="RecruitGuideEntry"/> data, keyed by friend area, for the "add a
    /// Pokemon to this friend area as if legitimately recruited" feature.
    /// </summary>
    public static partial class RBRecruitGuide
    {
        public static IReadOnlyList<RecruitGuideEntry> Entries => GeneratedEntries;

        private static IReadOnlyDictionary<int, RBFriendArea>? _speciesToFriendArea;

        private static IReadOnlyDictionary<int, RBFriendArea> SpeciesToFriendArea =>
            _speciesToFriendArea ??= GeneratedEntries
                .GroupBy(e => e.SpeciesId)
                .ToDictionary(g => g.Key, g => g.First().FriendArea);

        /// <summary>
        /// All species whose easiest real recruit spot puts them in the given friend area.
        /// </summary>
        public static IEnumerable<RecruitGuideEntry> GetCandidates(RBFriendArea area)
        {
            return GeneratedEntries.Where(e => e.FriendArea == area);
        }

        /// <summary>
        /// The friend area a given species calls home, if it's one of the 204 species this
        /// guide covers. Used to figure out which already-recruited Pokemon in a save are
        /// "living in" a given friend area, since the save itself doesn't track that -- a
        /// recruited Pokemon just belongs to its species' one static home area.
        /// </summary>
        public static RBFriendArea? HomeAreaOf(int speciesId)
        {
            return SpeciesToFriendArea.TryGetValue(speciesId, out var area) ? area : null;
        }

        /// <summary>
        /// Builds an <see cref="RBStoredPokemon"/> matching this entry: real recruit-level
        /// stats, the dungeon/floor it was met at, IQ 1 (the same starting IQ every
        /// freshly-recruited or freshly-hatched Pokemon has in the decomp, e.g.
        /// <c>src/dungeon_mon_recruit.c</c> and <c>src/pokemon.c</c>), and the moveset a wild
        /// spawn at this level would carry (<see cref="RBLearnsets.ApplyWildMoveset"/>).
        /// </summary>
        public static RBStoredPokemon ToStoredPokemon(this RecruitGuideEntry entry)
        {
            var pokemon = new RBStoredPokemon
            {
                ID = entry.SpeciesId,
                Name = entry.SpeciesName,
                Level = entry.Level,
                MetAt = entry.DungeonId,
                Floor = entry.Floor,
                IQ = 1,
                HP = entry.HP,
                Attack = entry.Attack,
                SpAttack = entry.SpAttack,
                Defense = entry.Defense,
                SpDefense = entry.SpDefense,
                Exp = entry.Exp,
                Attack1 = new RBAttack(),
                Attack2 = new RBAttack(),
                Attack3 = new RBAttack(),
                Attack4 = new RBAttack(),
            };
            RBLearnsets.ApplyWildMoveset(pokemon);
            return pokemon;
        }
    }
}
