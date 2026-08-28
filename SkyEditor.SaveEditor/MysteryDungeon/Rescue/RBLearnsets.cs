using System;
using System.Collections.Generic;

namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    /// <summary>One entry of a species' level-up learnset.</summary>
    public readonly record struct RBLearnsetMove(int MoveId, int Level);

    /// <summary>
    /// Per-species level-up learnsets, decoded from the ROM's own <c>wazapara</c> data (the exact
    /// table <c>GetLevelUpMoves</c> in src/moves.c reads in-game) -- see
    /// tools/build_learnsets.py and RBLearnsetData.generated.cs.
    /// </summary>
    /// <remarks>
    /// The game gives a wild Pokemon its moves at spawn time (<c>sub_8072AC8</c>,
    /// src/dungeon_leveling.c, called from dungeon_mon_spawn.c): walk the learnset in order,
    /// fill the four slots with the first four moves learned at or below the spawn level, then
    /// each later qualifying move overwrites a uniformly random slot. Recruiting copies the
    /// spawned entity's moves verbatim, so a legitimate recruit's moveset is some outcome of
    /// that process -- verified against the reference save, where every fresh low-level recruit
    /// (Aron, Doduo, Magnemite) is the deterministic no-overwrite prefix and the higher-level
    /// ones (Poochyena Lv.20, both Duskull Lv.28) are exact random-overwrite outcomes, down to
    /// slot order.
    /// </remarks>
    public static partial class RBLearnsets
    {
        /// <summary>
        /// The level-up learnset for <paramref name="speciesId"/>, in the ROM's own order
        /// (ascending level; ties keep table order). Empty for IDs without learnset data
        /// (0, anything past 419 -- including Munchlax, which the game special-cases to an
        /// empty learnset in <c>GetLevelUpMoves</c>).
        /// </summary>
        public static IReadOnlyList<RBLearnsetMove> LevelUpMoves(int speciesId)
        {
            if (speciesId < 1 || speciesId >= GeneratedLevelUpMoves.Length)
            {
                return Array.Empty<RBLearnsetMove>();
            }
            var raw = GeneratedLevelUpMoves[speciesId];
            var result = new RBLearnsetMove[raw.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new RBLearnsetMove(raw[i * 2], raw[i * 2 + 1]);
            }
            return result;
        }

        /// <summary>
        /// A legitimate wild-spawn moveset for this species at this level: the last (up to) four
        /// learnset moves learned at or below <paramref name="level"/>, in learnset order.
        /// </summary>
        /// <remarks>
        /// The game's own choice is randomized (see the class remarks): the first four learnset
        /// moves fill the slots, then each later one overwrites a random slot, so the final
        /// learnset move is always present but earlier ones survive by luck. This deterministic
        /// pick is one of the reachable outcomes (each of the last four moves landing in a
        /// distinct slot) and the one players expect: the species' most recent moves.
        /// </remarks>
        public static IReadOnlyList<int> WildMoveset(int speciesId, int level)
        {
            var learnset = LevelUpMoves(speciesId);
            int known = 0;
            while (known < learnset.Count && learnset[known].Level <= level)
            {
                known++;
            }
            var count = Math.Min(4, known);
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = learnset[known - count + i].MoveId;
            }
            return result;
        }

        /// <summary>
        /// Sets <paramref name="pokemon"/>'s four move slots to <see cref="WildMoveset"/> for its
        /// species and level, with the flags a legitimately recruited Pokemon's moves carry in a
        /// real save (valid, AI-usable, nothing else -- verified against every organic recruit in
        /// the reference save).
        /// </summary>
        public static void ApplyWildMoveset(RBStoredPokemon pokemon)
        {
            var moves = WildMoveset(pokemon.ID, pokemon.Level);
            var attacks = new[] { pokemon.Attack1, pokemon.Attack2, pokemon.Attack3, pokemon.Attack4 };
            for (int i = 0; i < attacks.Length; i++)
            {
                var attack = attacks[i] ?? new RBAttack();
                if (i < moves.Count)
                {
                    attack.IsValid = true;
                    attack.IsSwitched = true;
                    attack.ID = moves[i];
                }
                else
                {
                    attack.IsValid = false;
                    attack.IsSwitched = false;
                    attack.ID = 0;
                }
                attack.IsLinked = false;
                attack.IsSet = false;
                attack.PowerBoost = 0;
                attacks[i] = attack;
            }
            pokemon.Attack1 = attacks[0];
            pokemon.Attack2 = attacks[1];
            pokemon.Attack3 = attacks[2];
            pokemon.Attack4 = attacks[3];
        }
    }
}
