using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    /// <summary>
    /// The extracted level-up learnsets (<see cref="RBLearnsets"/>) and the wild-spawn moveset
    /// rule, verified against the same organic save the algebra tests use: several roster members
    /// there are fresh recruits still carrying exactly the moves the game's spawn code
    /// (sub_8072AC8, src/dungeon_leveling.c) gave them, which pins both the extracted data and
    /// the moveset rule to reality.
    /// </summary>
    [TestClass]
    public class RBLearnsetTests
    {
        private const string Category = "RB Learnset Tests";

        private static RBSave LoadSave() => new RBSave(DataUtil.GetBinaryResource("RRT.sav"));

        [TestMethod]
        [TestCategory(Category)]
        public void LevelUpMoves_Bulbasaur_MatchesTheKnownLearnsetExactly()
        {
            var names = RBLearnsets.LevelUpMoves(1)
                .Select(m => $"{Lists.RBMoves[m.MoveId]}@{m.Level}")
                .ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "Tackle@1", "Growl@4", "Leech Seed@7", "Vine Whip@10", "Poisonpowder@15",
                "Sleep Powder@15", "Razor Leaf@20", "Sweet Scent@25", "Growth@32",
                "Synthesis@39", "Solarbeam@46",
            }, names);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void LevelUpMoves_EverySpecies_IsSortedAndUsesRealMoveIds()
        {
            for (int species = 1; species <= 419; species++)
            {
                var learnset = RBLearnsets.LevelUpMoves(species);
                Assert.IsTrue(learnset.Count > 0, $"species {species} has an empty learnset");
                for (int i = 0; i < learnset.Count; i++)
                {
                    Assert.IsTrue(Lists.RBMoves.ContainsKey(learnset[i].MoveId),
                        $"species {species}: move id {learnset[i].MoveId} is not a real move");
                    if (i > 0)
                    {
                        Assert.IsTrue(learnset[i].Level >= learnset[i - 1].Level,
                            $"species {species}: levels not sorted (the game's readers early-out on the first level past the target)");
                    }
                }
            }
        }

        [TestMethod]
        [TestCategory(Category)]
        public void LevelUpMoves_SpeciesWithoutData_AreEmpty()
        {
            Assert.AreEqual(0, RBLearnsets.LevelUpMoves(0).Count);
            Assert.AreEqual(0, RBLearnsets.LevelUpMoves(420).Count, "Munchlax is special-cased to an empty learnset in GetLevelUpMoves");
            Assert.AreEqual(0, RBLearnsets.LevelUpMoves(-1).Count);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void WildMoveset_MatchesFreshOrganicRecruitsInTheRealSave()
        {
            // These roster members were recruited at their current level and never leveled,
            // taught, or rearranged afterward, and few enough learnset moves qualify that the
            // game's random-overwrite step never fired -- so their saved moves are the unique
            // spawn outcome, which WildMoveset must reproduce exactly (ids and slot order).
            var save = LoadSave();
            foreach (var slot in new[] { 55 /* Aron Lv.6 */, 110 /* Doduo Lv.16 */, 317 /* Magnemite Lv.6 */ })
            {
                var pkm = save.StoredPokemon.Find(p => p.SlotIndex == slot)!;
                var expected = new[] { pkm.Attack1, pkm.Attack2, pkm.Attack3, pkm.Attack4 }
                    .Where(a => a.IsValid)
                    .Select(a => a.ID)
                    .ToArray();
                var actual = RBLearnsets.WildMoveset(pkm.ID, pkm.Level).ToArray();
                CollectionAssert.AreEqual(expected, actual, $"slot {slot} (#{pkm.ID} Lv.{pkm.Level})");
            }
        }

        [TestMethod]
        [TestCategory(Category)]
        public void WildMoveset_TakesTheLastFourQualifyingMoves()
        {
            // Poochyena (#286) at Lv.20 knows 5 learnset moves (Tackle@1 Howl@5 Sand-Attack@9
            // Bite@13 Odor Sleuth@17), so the deterministic pick drops the oldest. The organic
            // save's own Lv.20 Poochyena is a different, RNG-chosen outcome of the same spawn
            // process (Odor Sleuth overwrote its slot 3) -- both are legitimate.
            var moves = RBLearnsets.WildMoveset(286, 20).Select(id => Lists.RBMoves[id]).ToArray();
            CollectionAssert.AreEqual(new[] { "Howl", "Sand-Attack", "Bite", "Odor Sleuth" }, moves);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void ApplyWildMoveset_SetsTheFlagsOrganicRecruitsCarry()
        {
            // Every organic recruit's real moves are valid + AI-usable (IsSwitched) and nothing
            // else; empty slots are fully zeroed.
            var pkm = new RecruitGuideEntry(329, "Aron", RBFriendArea.MagneticQuarry, 2, "Mt. Steel", 6, 6, 25, 12, 8, 15, 9, 2000)
                .ToStoredPokemon();

            Assert.IsTrue(pkm.Attack1.IsValid && pkm.Attack1.IsSwitched);
            Assert.AreEqual("Tackle", Lists.RBMoves[pkm.Attack1.ID]);
            Assert.IsTrue(pkm.Attack2.IsValid && pkm.Attack2.IsSwitched);
            Assert.AreEqual("Harden", Lists.RBMoves[pkm.Attack2.ID]);
            foreach (var empty in new[] { pkm.Attack3, pkm.Attack4 })
            {
                Assert.IsFalse(empty.IsValid || empty.IsSwitched || empty.IsLinked || empty.IsSet);
                Assert.AreEqual(0, empty.ID);
                Assert.AreEqual(0, empty.PowerBoost);
            }
        }
    }
}
