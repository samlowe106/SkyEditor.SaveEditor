using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    /// <summary>
    /// Tests against a real, human-played Red Rescue Team save (RRT.sav), as opposed to
    /// BRT.sav's synthetic/unknown-provenance Blue Rescue Team save. Sourced from a real
    /// mGBA .srm (exactly 128KB, matching SAVE_FORMAT.md's derived physical media size with
    /// no padding), dumped against a ROM whose SHA-1 matches the pret/pmd-red decomp's
    /// red.sha1 exactly.
    /// </summary>
    [TestClass]
    public class RRTSaveDataTests
    {
        private const string Category = "RRT Real Save Data Tests";

        private RBSave GetTestSave()
        {
            return new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void TestChecksumsValid()
        {
            var save = GetTestSave();
            Assert.IsTrue(save.IsPrimaryChecksumValid());
            Assert.IsTrue(save.IsSecondaryChecksumValid());
            Assert.AreEqual(save.PrimaryChecksum, save.SecondaryChecksum, "Primary and backup checksums should match on an unmodified save.");
        }

        [TestMethod]
        [TestCategory(Category)]
        public void TeamName_DecodesToKnownValue()
        {
            var save = GetTestSave();
            Assert.AreEqual("Pokémon", save.TeamName);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void General_DecodesToKnownValues()
        {
            var save = GetTestSave();
            Assert.AreEqual(1410, save.RescueTeamPoints);
            Assert.AreEqual(3, save.HeldMoney);
            Assert.AreEqual(290, save.StoredMoney);
        }

        /// <summary>
        /// Regression test for a serious bug: the roster is a fixed 413-slot array with gaps
        /// between occupied slots, not a compact list. The old loading logic stopped at the
        /// first empty slot -- which is slot 0 on this real save -- so it silently returned
        /// zero recruited Pokemon instead of the real 20. Values below were independently
        /// decoded from the raw save bytes in Python and cross-checked against this fix.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public void StoredPokemon_LoadsFullSparseRoster()
        {
            var save = GetTestSave();

            var expected = new (int Slot, int Species, int Level)[]
            {
                (54, 4, 32), (55, 329, 6), (70, 288, 14), (71, 288, 14), (72, 83, 12),
                (73, 286, 20), (95, 260, 5), (108, 32, 12), (109, 256, 14), (110, 84, 16),
                (136, 13, 9), (186, 298, 16), (187, 298, 16), (188, 298, 16), (225, 7, 27),
                (317, 81, 6), (371, 387, 21), (372, 383, 28), (373, 383, 28), (398, 21, 5),
            };

            Assert.AreEqual(expected.Length, save.StoredPokemon.Count);
            foreach (var (slot, species, level) in expected)
            {
                var pkm = save.StoredPokemon.Find(p => p.SlotIndex == slot);
                Assert.IsNotNull(pkm, $"Expected a Pokemon in slot {slot}.");
                Assert.AreEqual(species, pkm!.ID, $"Wrong species in slot {slot}.");
                Assert.AreEqual(level, pkm.Level, $"Wrong level in slot {slot}.");
            }
        }

        [TestMethod]
        [TestCategory(Category)]
        public void StoredPokemon_RoundTripsWithoutRelocatingExistingSlots()
        {
            var save = GetTestSave();
            var originalSlots = save.StoredPokemon.ConvertAll(p => p.SlotIndex);

            var savedBytes = save.ToByteArray();
            var reloaded = new RBSave(savedBytes);

            Assert.IsTrue(reloaded.IsPrimaryChecksumValid());
            var reloadedSlots = reloaded.StoredPokemon.ConvertAll(p => p.SlotIndex);
            originalSlots.Sort();
            reloadedSlots.Sort();
            Assert.AreEqual(save.StoredPokemon.Count, reloaded.StoredPokemon.Count,
                $"original=[{string.Join(",", originalSlots)}] reloaded=[{string.Join(",", reloadedSlots)}]");
            CollectionAssert.AreEqual(originalSlots, reloadedSlots, "Re-saving an untouched roster should not relocate any existing Pokemon to a different slot.");
        }

        /// <summary>
        /// Sanity check for RBOffsets.FriendAreaOffset: on a save with 20 recruited Pokemon, at
        /// least some friend areas should be unlocked, and the unused index 0 (RBFriendArea.None)
        /// should never be set by the game.
        /// </summary>
        [TestMethod]
        [TestCategory(Category)]
        public void FriendAreasUnlocked_DecodesToPlausibleValues()
        {
            var save = GetTestSave();

            Assert.AreEqual(58, save.FriendAreasUnlocked.Length);
            Assert.IsFalse(save.FriendAreasUnlocked[(int)RBFriendArea.None]);
            Assert.IsTrue(System.Array.Exists(save.FriendAreasUnlocked, unlocked => unlocked), "Expected at least one unlocked friend area on a save with 20 recruited Pokemon.");
        }

        [TestMethod]
        [TestCategory(Category)]
        public void MarkBossRecruited_OnRealSave_AddsToEmptySlotWithoutDisturbingExistingRoster()
        {
            var save = GetTestSave();
            var originalSlots = save.StoredPokemon.ConvertAll(p => p.SlotIndex);

            save.MarkBossRecruited(RBBossEncounters.Zapdos, new RBStoredPokemon
            {
                ID = RBBossEncounters.Zapdos,
                Name = "Zapdos",
                Level = 40,
                Attack1 = new RBAttack(),
                Attack2 = new RBAttack(),
                Attack3 = new RBAttack(),
                Attack4 = new RBAttack(),
            });

            var savedBytes = save.ToByteArray();
            var reloaded = new RBSave(savedBytes);

            Assert.IsTrue(reloaded.IsPrimaryChecksumValid());
            Assert.AreEqual(originalSlots.Count + 1, reloaded.StoredPokemon.Count);
            foreach (var slot in originalSlots)
            {
                var pkm = reloaded.StoredPokemon.Find(p => p.SlotIndex == slot);
                Assert.IsNotNull(pkm, $"Pre-existing Pokemon in slot {slot} should still be there after adding a new one.");
            }
            var zapdos = reloaded.StoredPokemon.Find(p => p.ID == RBBossEncounters.Zapdos);
            Assert.IsNotNull(zapdos);
            Assert.IsFalse(originalSlots.Contains(zapdos!.SlotIndex), "The newly-added Pokemon should land in a slot that wasn't already occupied.");
        }

        [TestMethod]
        [TestCategory(Category)]
        public void NumPokemonRecruited_DecodesToKnownValue()
        {
            // Confirmed against the real cartridge's Adventure Log screen ("Pokemon recruited:
            // 18") at the time this save was dumped. This anchors AdventureDataOffset and
            // NumPokemonRecruitedOffset (both measured from the start of AdventureBits); it says
            // nothing about AdventureBitsBitLength, which only affects where the *next* section
            // (ExclusivePokemonData) begins. See that offset's remarks in RBSave.cs.
            var save = GetTestSave();
            Assert.AreEqual(18, save.NumPokemonRecruited);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void StoryFlags_CoverEveryNamedCutsceneFlagExactlyOnce()
        {
            var flags = RBStoryFlags.All.Select(i => i.Flag).ToList();
            CollectionAssert.AllItemsAreUnique(flags);
            CollectionAssert.AreEquivalent(Enum.GetValues<RBCutsceneFlag>(), flags);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void StoryFlags_MatchThisSavesKnownPlaythroughState()
        {
            // This save is a verified early-postgame playthrough: the whole main story is done
            // (through Rayquaza), and of the postgame content only Howling Forest (the Wonder
            // Mail Smeargle-join dungeon -- there's a Smeargle in the roster) is complete. This
            // doubles as a regression test for RBOffsets.AdventureBitsBitLength: a shifted
            // ExclusivePokemonData offset breaks the contiguous main-story run immediately.
            var save = GetTestSave();

            foreach (var info in RBStoryFlags.All)
            {
                var set = save.ExclusivePokemonData.GetCutsceneFlag(info.Flag);
                var expected = info.Phase == RBStoryPhase.MainStory || info.Flag == RBCutsceneFlag.HowlingForestComplete;
                Assert.AreEqual(expected, set, $"{info.Flag} should be {(expected ? "set" : "unset")} on this save.");
            }
        }

        [TestMethod]
        [TestCategory(Category)]
        public void ToolboxAndHeldItems_DecodeToKnownValues()
        {
            // Verified against an independent bit-level decode of this save: the Toolbox holds
            // 10 items led by a stack of 81 Gravelerocks and ending with the Dive TM, and five
            // roster Pokemon hold gear (slot 54 "Sam" the Charmander holds a Special Band,
            // id 33). Anchors both the 20-slot Toolbox model and the held-item bit positions
            // inside the 323-bit roster slot (id at bits 148-155, quantity at 156-162).
            var save = GetTestSave();

            Assert.AreEqual(10, save.HeldItems.Count);
            Assert.AreEqual(7, save.HeldItems[0].ID);            // Gravelerock
            Assert.AreEqual(81, save.HeldItems[0].Parameter);
            Assert.AreEqual(231, save.HeldItems[9].ID);          // Dive TM
            Assert.AreEqual(0, save.HeldItems[9].Parameter);

            var sam = save.StoredPokemon.Find(p => p.SlotIndex == 54)!;
            Assert.AreEqual(33, sam.HeldItemId);                 // Special Band
            Assert.AreEqual(0, sam.HeldItemQuantity);
            Assert.AreEqual(5, save.StoredPokemon.Count(p => p.HeldItemId != 0));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void ToolboxAndHeldItems_EditsRoundTrip()
        {
            var save = GetTestSave();
            var aron = save.StoredPokemon.Find(p => p.SlotIndex == 55)!;
            Assert.AreEqual(0, aron.HeldItemId);
            aron.HeldItemId = 55; // Oran Berry
            save.HeldItems.RemoveAt(1); // drop the Max Elixir
            save.HeldItems.Add(new RBHeldItem { ID = 58, Parameter = 0 }); // Reviver Seed

            var reloaded = new RBSave(save.ToByteArray());

            Assert.IsTrue(reloaded.IsPrimaryChecksumValid());
            Assert.AreEqual(55, reloaded.StoredPokemon.Find(p => p.SlotIndex == 55)!.HeldItemId);
            Assert.AreEqual(10, reloaded.HeldItems.Count);
            Assert.AreEqual(58, reloaded.HeldItems[9].ID);
            // The other roster held items are untouched.
            Assert.AreEqual(33, reloaded.StoredPokemon.Find(p => p.SlotIndex == 54)!.HeldItemId);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void HasRecruitedSpeciesFlag_MatchesKnownRoster()
        {
            var save = GetTestSave();

            // Charmander (species 4) is part of this save's real, pre-tool-edited roster.
            Assert.IsTrue(save.HasRecruitedSpeciesFlag(4));

            // Mewtwo (species 150) was never recruited on this save.
            Assert.IsFalse(save.HasRecruitedSpeciesFlag(150));
        }
    }
}
