using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    [TestClass]
    public class RBFriendAreaCapacityTests
    {
        private const string Category = "RB Friend Area Capacity Tests";

        [TestMethod]
        [TestCategory(Category)]
        public void Capacities_SumToStoredPokemonCount()
        {
            var total = Enum.GetValues<RBFriendArea>().Sum(a => RBFriendAreaCapacity.Capacity(a));
            Assert.AreEqual(new RBSave.RBOffsets().StoredPokemonCount, total);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void None_HasZeroCapacity()
        {
            Assert.AreEqual(0, RBFriendAreaCapacity.Capacity(RBFriendArea.None));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void SlotRanges_AreContiguousAndNonOverlapping()
        {
            var expectedStart = 0;
            foreach (var area in Enum.GetValues<RBFriendArea>())
            {
                var (start, count) = RBFriendAreaCapacity.SlotRange(area);
                Assert.AreEqual(expectedStart, start, $"{area} should start right after the previous area's range.");
                Assert.AreEqual(RBFriendAreaCapacity.Capacity(area), count);
                expectedStart += count;
            }

            Assert.AreEqual(new RBSave.RBOffsets().StoredPokemonCount, expectedStart, "The last area's range should end exactly at the roster's slot count.");
        }
    }
}
