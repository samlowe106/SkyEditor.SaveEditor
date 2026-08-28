using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    [TestClass]
    public class RBSavePendingChangesTests
    {
        private const string Category = "RB Save Pending Changes Tests";

        [TestMethod]
        [TestCategory(Category)]
        public void FreshlyLoadedSave_HasNoPendingChanges()
        {
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));

            foreach (var pkm in save.StoredPokemon)
            {
                Assert.IsFalse(save.IsSlotPending(pkm.SlotIndex), $"Slot {pkm.SlotIndex} should not be pending right after load.");
            }

            foreach (var area in System.Enum.GetValues<RBFriendArea>())
            {
                if (area == RBFriendArea.None) continue;
                Assert.IsFalse(save.IsFriendAreaPending(area), $"{area} should not be pending right after load.");
            }

            Assert.AreEqual(0, save.HeldMoneyDelta);
            Assert.AreEqual(0, save.StoredMoneyDelta);
            foreach (var item in save.StoredItems)
            {
                Assert.AreEqual(0, save.PendingItemDelta(item.ItemID), $"Item {item.ItemID} should have no pending delta right after load.");
            }
        }

        [TestMethod]
        [TestCategory(Category)]
        public void RecruitFromGuide_MarksNewSlotAndFriendAreaWherePending()
        {
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            var entry = RBRecruitGuide.GetCandidates(RBFriendArea.SeafloorCave).Single(c => c.SpeciesName == "Kyogre");

            var added = save.RecruitFromGuide(entry);

            Assert.IsTrue(save.IsSlotPending(added.SlotIndex));
            Assert.IsTrue(save.IsFriendAreaPending(RBFriendArea.SeafloorCave));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void AfterSave_PendingChangesClearOnTheSameInstance()
        {
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            var entry = RBRecruitGuide.GetCandidates(RBFriendArea.SeafloorCave).Single(c => c.SpeciesName == "Kyogre");
            var added = save.RecruitFromGuide(entry);

            Assert.IsTrue(save.IsSlotPending(added.SlotIndex));

            save.ToByteArray();

            Assert.IsFalse(save.IsSlotPending(added.SlotIndex), "Saving should refresh the pending-changes snapshot so the just-saved slot stops reading as pending.");
            Assert.IsFalse(save.IsFriendAreaPending(RBFriendArea.SeafloorCave));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void MoneyEdits_TrackAsPendingUntilSaved()
        {
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            save.HeldMoney += 500;
            save.StoredMoney -= 200;

            Assert.AreEqual(500, save.HeldMoneyDelta);
            Assert.AreEqual(-200, save.StoredMoneyDelta);

            save.ToByteArray();

            Assert.AreEqual(0, save.HeldMoneyDelta);
            Assert.AreEqual(0, save.StoredMoneyDelta);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void AddedItem_TracksPendingDeltaUntilSaved()
        {
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            const int itemId = 1;
            save.StoredItems.Add(new RBStoredItem(itemId, 3));

            Assert.AreEqual(3, save.PendingItemDelta(itemId));

            save.ToByteArray();

            Assert.AreEqual(0, save.PendingItemDelta(itemId));
        }
    }
}
