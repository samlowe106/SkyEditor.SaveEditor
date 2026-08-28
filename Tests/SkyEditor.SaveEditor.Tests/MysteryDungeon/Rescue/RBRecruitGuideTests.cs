using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    [TestClass]
    public class RBRecruitGuideTests
    {
        private const string Category = "RB Recruit Guide Tests";

        [TestMethod]
        [TestCategory(Category)]
        public void Entries_HasAllSpeciesFromGuideMd()
        {
            Assert.AreEqual(204, RBRecruitGuide.Entries.Count);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void Entries_AllHaveSaneStats()
        {
            foreach (var entry in RBRecruitGuide.Entries)
            {
                Assert.IsTrue(entry.Level is >= 1 and <= 100, $"{entry.SpeciesName}: level {entry.Level} out of range");
                Assert.IsTrue(entry.HP > 0, $"{entry.SpeciesName}: HP should be positive");
                Assert.IsTrue(entry.Attack > 0 && entry.SpAttack > 0 && entry.Defense > 0, $"{entry.SpeciesName}: offense/defense should be positive");
                Assert.IsTrue(entry.Exp >= 0, $"{entry.SpeciesName}: Exp should be non-negative");
                Assert.IsTrue(entry.Floor >= 1, $"{entry.SpeciesName}: floor should be at least 1");
            }
        }

        [TestMethod]
        [TestCategory(Category)]
        public void GetCandidates_ReturnsOnlySpeciesForThatArea()
        {
            var candidates = RBRecruitGuide.GetCandidates(RBFriendArea.LegendaryIsland);

            CollectionAssert.AreEquivalent(
                new[] { "Articuno", "Zapdos", "Moltres" },
                candidates.Select(c => c.SpeciesName).ToList());
        }

        [TestMethod]
        [TestCategory(Category)]
        public void ToStoredPokemon_RoundTripsDungeonAndFloor()
        {
            var entry = RBRecruitGuide.GetCandidates(RBFriendArea.LegendaryIsland).Single(c => c.SpeciesName == "Zapdos");

            var pokemon = entry.ToStoredPokemon();
            var roundTripped = new RBStoredPokemon(pokemon.GetStoredPokemonBits());

            Assert.AreEqual(entry.SpeciesId, roundTripped.ID);
            Assert.AreEqual(entry.Level, roundTripped.Level);
            Assert.AreEqual(entry.DungeonId, roundTripped.MetAt);
            Assert.AreEqual(entry.Floor, roundTripped.Floor);
            Assert.AreEqual(entry.HP, roundTripped.HP);
            Assert.AreEqual(entry.Exp, roundTripped.Exp);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void RecruitFromGuide_OnRealSave_AddsPlausibleRecruitWithValidChecksum()
        {
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            var entry = RBRecruitGuide.GetCandidates(RBFriendArea.SeafloorCave).Single(c => c.SpeciesName == "Kyogre");
            var numRecruitedBefore = save.NumPokemonRecruited;
            Assert.IsFalse(save.HasRecruitedSpeciesFlag(entry.SpeciesId));

            var added = save.RecruitFromGuide(entry);

            Assert.AreEqual("Kyogre", added.Name);
            Assert.AreEqual(entry.Level, added.Level);
            Assert.IsTrue(save.FriendAreasUnlocked[(int)RBFriendArea.SeafloorCave]);

            // The Adventure Log's recruited-count/species-flag are only updated at save time
            // (see UpdateAdventureLogForRosterChanges), not immediately here.
            Assert.AreEqual(numRecruitedBefore, save.NumPokemonRecruited);
            Assert.IsFalse(save.HasRecruitedSpeciesFlag(entry.SpeciesId));

            var (start, count) = RBFriendAreaCapacity.SlotRange(RBFriendArea.SeafloorCave);
            Assert.IsTrue(added.SlotIndex >= start && added.SlotIndex < start + count);

            var savedBytes = save.ToByteArray();
            var reloaded = new RBSave(savedBytes);
            Assert.IsTrue(reloaded.IsPrimaryChecksumValid());

            var reloadedKyogre = reloaded.StoredPokemon.Find(p => p.ID == entry.SpeciesId);
            Assert.IsNotNull(reloadedKyogre);
            Assert.AreEqual(entry.DungeonId, reloadedKyogre!.MetAt);
            Assert.AreEqual(entry.Floor, reloadedKyogre.Floor);
            Assert.AreEqual(added.SlotIndex, reloadedKyogre.SlotIndex);
            Assert.AreEqual(numRecruitedBefore + 1, reloaded.NumPokemonRecruited);
            Assert.IsTrue(reloaded.HasRecruitedSpeciesFlag(entry.SpeciesId));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void RecruitFromGuide_ForABossSpecies_AlsoSetsItsCutsceneCompleteFlag()
        {
            // Zapdos/Articuno/Moltres are recruitable both from the Friend Areas and Roster UI
            // (RecruitFromGuide) and the Story Flags UI (MarkBossRecruited) -- whichever path
            // adds them, the game still expects the cutscene "complete" flag set alongside the
            // roster entry (see RBBossEncounters), or a pre-free-roam save will still replay the
            // first-encounter cutscene despite the Pokemon already being recruited.
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            var entry = RBRecruitGuide.GetCandidates(RBFriendArea.LegendaryIsland).Single(c => c.SpeciesName == "Zapdos");
            // The fixture save has already cleared Mt. Thunder (it's a post-story save), so
            // simulate a pre-encounter save by clearing the flag before recruiting.
            save.ExclusivePokemonData.SetCutsceneFlag(RBCutsceneFlag.MtThunderPeakComplete, false);
            Assert.IsFalse(save.ExclusivePokemonData.GetCutsceneFlag(RBCutsceneFlag.MtThunderPeakComplete));

            save.RecruitFromGuide(entry);

            Assert.IsTrue(save.ExclusivePokemonData.GetCutsceneFlag(RBCutsceneFlag.MtThunderPeakComplete));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void RecruitFromGuide_WhenFriendAreaIsFull_Throws()
        {
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            var entry = RBRecruitGuide.GetCandidates(RBFriendArea.SeafloorCave).Single(c => c.SpeciesName == "Kyogre");

            save.RecruitFromGuide(entry);
            Assert.AreEqual(-1, save.FindFreeSlotInFriendArea(RBFriendArea.SeafloorCave));
            Assert.ThrowsException<System.InvalidOperationException>(() => save.RecruitFromGuide(entry));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void NumPokemonRecruited_OnSave_OnlyCountsTheNetRosterChange()
        {
            // Add two, remove one, then save -- the Adventure Log should only move by +1, not
            // +2, since it's a diff against the roster as it was when the file was loaded,
            // computed once at save time (not one increment per RecruitFromGuide call).
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            var numRecruitedBefore = save.NumPokemonRecruited;
            var kyogreEntry = RBRecruitGuide.GetCandidates(RBFriendArea.SeafloorCave).Single(c => c.SpeciesName == "Kyogre");
            var zapdosEntry = RBRecruitGuide.GetCandidates(RBFriendArea.LegendaryIsland).Single(c => c.SpeciesName == "Zapdos");

            var kyogre = save.RecruitFromGuide(kyogreEntry);
            var zapdos = save.RecruitFromGuide(zapdosEntry);
            save.StoredPokemon.Remove(kyogre);

            var savedBytes = save.ToByteArray();
            var reloaded = new RBSave(savedBytes);

            Assert.AreEqual(numRecruitedBefore + 1, reloaded.NumPokemonRecruited);
            Assert.IsTrue(reloaded.HasRecruitedSpeciesFlag(zapdos.ID));
            Assert.IsFalse(reloaded.HasRecruitedSpeciesFlag(kyogreEntry.SpeciesId), "Kyogre was added and then removed before saving, so it should never have been counted as recruited.");
            Assert.IsNull(reloaded.StoredPokemon.Find(p => p.ID == kyogreEntry.SpeciesId));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void NumPokemonRecruited_AcrossTwoConsecutiveSaves_DoesNotDoubleCount()
        {
            // A second Save on the same in-memory RBSave (no reload in between) should only
            // count whatever changed since the *first* save, not re-diff against the original
            // load state again.
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            var numRecruitedBefore = save.NumPokemonRecruited;
            var kyogreEntry = RBRecruitGuide.GetCandidates(RBFriendArea.SeafloorCave).Single(c => c.SpeciesName == "Kyogre");
            var zapdosEntry = RBRecruitGuide.GetCandidates(RBFriendArea.LegendaryIsland).Single(c => c.SpeciesName == "Zapdos");

            save.RecruitFromGuide(kyogreEntry);
            save.ToByteArray();
            Assert.AreEqual(numRecruitedBefore + 1, save.NumPokemonRecruited);

            save.RecruitFromGuide(zapdosEntry);
            save.ToByteArray();
            Assert.AreEqual(numRecruitedBefore + 2, save.NumPokemonRecruited);
        }
    }
}
