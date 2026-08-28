using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    /// <summary>The two 7-bit evolution-history levels decoded out of <c>Unk1</c>.</summary>
    [TestClass]
    public class RBEvolutionHistoryTests
    {
        private const string Category = "RB Evolution History Tests";

        [TestMethod]
        [TestCategory(Category)]
        public void NeverEvolvedOrganicRoster_ReadsZeroForBothLevels()
        {
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            Assert.AreEqual(20, save.StoredPokemon.Count);
            foreach (var pkm in save.StoredPokemon)
            {
                Assert.AreEqual((0, 0), (pkm.FirstEvolutionLevel, pkm.SecondEvolutionLevel), $"slot {pkm.SlotIndex}");
            }
        }

        [TestMethod]
        [TestCategory(Category)]
        public void EvolutionLevels_RoundTripThroughTheFileWithoutTouchingNeighbors()
        {
            var original = DataUtil.GetBinaryResource("RRT.sav");
            var save = new RBSave(original);
            var hero = save.StoredPokemon.Find(p => p.SlotIndex == 54)!;
            hero.FirstEvolutionLevel = 16;
            hero.SecondEvolutionLevel = 36;

            var reloaded = new RBSave(save.ToByteArray());
            var back = reloaded.StoredPokemon.Find(p => p.SlotIndex == 54)!;
            Assert.AreEqual((16, 36), (back.FirstEvolutionLevel, back.SecondEvolutionLevel));
            // The fields sit between floor and IQ; both neighbors must be untouched.
            Assert.AreEqual((hero.MetAt, hero.Floor, hero.IQ, hero.Level), (back.MetAt, back.Floor, back.IQ, back.Level));

            back.FirstEvolutionLevel = 0;
            back.SecondEvolutionLevel = 0;
            CollectionAssert.AreEqual(original, reloaded.ToByteArray(), "reverting the edit must restore the original bytes exactly");
        }
    }
}
