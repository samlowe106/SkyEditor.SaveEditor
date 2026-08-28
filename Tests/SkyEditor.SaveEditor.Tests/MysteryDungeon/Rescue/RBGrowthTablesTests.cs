using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    /// <summary>
    /// The bundled growth tables (<see cref="RBGrowthTables"/>) and the level-change rule built on
    /// them, pinned against the independently generated recruit guide and the game's own caps.
    /// </summary>
    [TestClass]
    public class RBGrowthTablesTests
    {
        private const string Category = "RB Growth Table Tests";

        [TestMethod]
        [TestCategory(Category)]
        public void BasePlusSummedGains_ReproducesEveryRecruitGuideEntryExactly()
        {
            // RBRecruitGuideData was produced by a separate script (build_recruit_guide.py) from
            // the same ROM tables and spot-checked against the real save; the bundled binary must
            // agree with it for all 204 species/level pairs, stats and Exp alike.
            foreach (var entry in RBRecruitGuide.Entries)
            {
                var pkm = new RBStoredPokemon { ID = entry.SpeciesId, Level = 1 };
                var b = RBGrowthTables.BaseStats(entry.SpeciesId)!.Value;
                pkm.HP = b.HP; pkm.Attack = b.Attack; pkm.SpAttack = b.SpAttack; pkm.Defense = b.Defense; pkm.SpDefense = b.SpDefense;

                Assert.IsTrue(RBGrowthTables.SetLevel(pkm, entry.Level), $"{entry.SpeciesName}: no growth data");
                Assert.AreEqual((entry.HP, entry.Attack, entry.SpAttack, entry.Defense, entry.SpDefense, entry.Exp),
                    (pkm.HP, pkm.Attack, pkm.SpAttack, pkm.Defense, pkm.SpDefense, pkm.Exp),
                    $"{entry.SpeciesName} Lv.{entry.Level}");
            }
        }

        [TestMethod]
        [TestCategory(Category)]
        public void SetLevel_DownThenUp_IsExactlyInverseAwayFromTheCaps()
        {
            var charmander = RBRecruitGuide.Entries.First(e => e.SpeciesId == 4).ToStoredPokemon(); // Lv.30
            var snapshot = (charmander.HP, charmander.Attack, charmander.SpAttack, charmander.Defense, charmander.SpDefense, charmander.Exp);

            RBGrowthTables.SetLevel(charmander, 12);
            Assert.AreEqual(12, charmander.Level);
            Assert.AreEqual((int)RBGrowthTables.ExpRequiredForLevel(4, 12)!.Value, charmander.Exp, "Exp snaps to the new level's requirement");
            Assert.IsTrue(charmander.HP < snapshot.HP);

            RBGrowthTables.SetLevel(charmander, 30);
            Assert.AreEqual(snapshot, (charmander.HP, charmander.Attack, charmander.SpAttack, charmander.Defense, charmander.SpDefense, charmander.Exp));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void SetLevel_RespectsTheGamesCapsAndFloors()
        {
            var pkm = new RBStoredPokemon { ID = 150, Level = 1, HP = 998, Attack = 254, SpAttack = 254, Defense = 254, SpDefense = 254 };
            RBGrowthTables.SetLevel(pkm, 100);
            Assert.AreEqual((999, 255, 255, 255, 255), (pkm.HP, pkm.Attack, pkm.SpAttack, pkm.Defense, pkm.SpDefense));

            var weak = new RBStoredPokemon { ID = 150, Level = 100, HP = 2, Attack = 2, SpAttack = 2, Defense = 2, SpDefense = 2 };
            RBGrowthTables.SetLevel(weak, 1);
            Assert.AreEqual((1, 1, 1, 1, 1), (weak.HP, weak.Attack, weak.SpAttack, weak.Defense, weak.SpDefense));
            Assert.AreEqual(0, weak.Exp);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void LevelForExp_IsTheGamesLevelUpThreshold()
        {
            var lv30 = (int)RBGrowthTables.ExpRequiredForLevel(4, 30)!.Value; // Charmander: 112290
            Assert.AreEqual(112290, lv30);
            Assert.AreEqual(29, RBGrowthTables.LevelForExp(4, lv30 - 1));
            Assert.AreEqual(30, RBGrowthTables.LevelForExp(4, lv30));
            Assert.AreEqual(30, RBGrowthTables.LevelForExp(4, lv30 + 5));
            Assert.AreEqual(1, RBGrowthTables.LevelForExp(4, 0));
            Assert.IsNull(RBGrowthTables.LevelForExp(0, 500));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void SetLevel_KeepExp_LeavesTypedExpAlone()
        {
            var pkm = RBRecruitGuide.Entries.First(e => e.SpeciesId == 4).ToStoredPokemon();
            pkm.Exp = 112290 + 5;
            RBGrowthTables.SetLevel(pkm, 31, keepExp: true);
            Assert.AreEqual(112295, pkm.Exp);
            Assert.AreEqual(31, pkm.Level);
        }
    }
}
