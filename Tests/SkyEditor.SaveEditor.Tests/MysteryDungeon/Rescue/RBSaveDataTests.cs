using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    [TestClass]
    public class RBSaveDataTests
    {
        [TestMethod]
        [TestCategory("RB Save Data Tests")]
        public void StoredItems_SurviveRoundTrip()
        {
            // Guards the held-item slot count: writing more than the bag's 20 slots
            // (INVENTORY_SIZE in the decomp) runs past the bag region and wipes the storage
            // quantities of the first ~68 item IDs. The fixture's storage spans IDs on both
            // sides of that boundary, so any regression shrinks this list immediately.
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            var original = save.StoredItems.ConvertAll(i => (i.ItemID, i.Quantity));
            Assert.IsTrue(original.Exists(i => i.ItemID <= 68), "Fixture should hold storage items in the vulnerable low-ID range.");

            var reloaded = new RBSave(save.ToByteArray());
            var roundTripped = reloaded.StoredItems.ConvertAll(i => (i.ItemID, i.Quantity));

            CollectionAssert.AreEqual(original, roundTripped);
            Assert.AreEqual(save.HeldItems.Count, reloaded.HeldItems.Count, "Held items should also survive unchanged.");
        }

        [TestMethod]
        [TestCategory("RB Save Data Tests")]
        public void PreSave_ClampsFieldsToTheGamesOwnLimits()
        {
            // Limits per the decomp: MAX_TEAM_MONEY 99999, MAX_TEAM_SAVINGS 9999999, IQ and
            // storage quantities clamped to 999, level cap 100. Values past these would either
            // mark the save as tool-edited or overflow their bitfields entirely.
            var save = new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
            var slot = save.StoredPokemon[0].SlotIndex;
            save.HeldMoney = 1_000_000;
            save.StoredMoney = 50_000_000;
            save.StoredPokemon[0].IQ = 5000;
            save.StoredPokemon[0].Level = 120;
            save.StoredItems.Add(new RBStoredItem(1, 2000));

            var reloaded = new RBSave(save.ToByteArray());

            Assert.AreEqual(99999, reloaded.HeldMoney);
            Assert.AreEqual(9999999, reloaded.StoredMoney);
            var pkm = reloaded.StoredPokemon.Find(p => p.SlotIndex == slot)!;
            Assert.AreEqual(999, pkm.IQ);
            Assert.AreEqual(100, pkm.Level);
            Assert.AreEqual(999, reloaded.StoredItems.Find(i => i.ItemID == 1)!.Quantity);
        }

        [TestMethod]
        [TestCategory("RB Save Data Tests")]
        public void Load_FallsBackToBackupBlock_WhenPrimaryIsCorrupt()
        {
            // Corrupt a 4KB swath of the primary block's roster region, leaving the backup
            // block (0x6000+) untouched. The loader must read everything from the backup, and
            // a subsequent save must produce a fully valid file with the original data. A
            // previous version of the fallback mixed byte and bit offsets (and some loaders
            // ignored the fallback entirely), which read garbage from ~0xC00 bytes in and would
            // then have written it back out under fresh, valid checksums.
            var bytes = DataUtil.GetBinaryResource("RRT.sav");
            for (int i = 0x448; i < 0x1448; i++)
            {
                bytes[i] ^= 0xFF;
            }

            var save = new RBSave(bytes);
            Assert.IsFalse(save.IsPrimaryChecksumValid());
            Assert.IsTrue(save.IsSecondaryChecksumValid());
            Assert.AreEqual(20, save.StoredPokemon.Count);
            Assert.IsTrue(save.StoredPokemon.Exists(p => p.Name == "Sam"));
            Assert.IsTrue(save.ExclusivePokemonData.GetCutsceneFlag(RBCutsceneFlag.SkyTowerSummitComplete));
            Assert.AreEqual(3, save.MailData.JobSlots.Count(m => !m.IsEmpty));

            var repaired = new RBSave(save.ToByteArray());
            Assert.IsTrue(repaired.IsPrimaryChecksumValid());
            Assert.IsTrue(repaired.IsSecondaryChecksumValid());
            Assert.AreEqual(20, repaired.StoredPokemon.Count);
            Assert.IsTrue(repaired.StoredPokemon.Exists(p => p.Name == "Sam"));
        }

        private const string Category = "RB Save Data Tests";

        private byte[] GetTestSaveData()
        {
            return DataUtil.GetBinaryResource("BRT.sav");
        }

        private RBSave GetTestSave()
        {
            return new RBSave(GetTestSaveData());
        }

        private static RBStoredPokemon CreateBossPokemon(int speciesId, string name)
        {
            return new RBStoredPokemon
            {
                ID = speciesId,
                Name = name,
                Level = 40,
                MetAt = 1,
                IQ = 100,
                HP = 150,
                Attack = 50,
                SpAttack = 50,
                Defense = 50,
                SpDefense = 50,
                Exp = 100000,
                Attack1 = new RBAttack(),
                Attack2 = new RBAttack(),
                Attack3 = new RBAttack(),
                Attack4 = new RBAttack(),
            };
        }

        [TestMethod]
        [TestCategory(Category)]
        public void TestChecksumsValid()
        {
            var save = GetTestSave();
            Assert.IsTrue(save.IsPrimaryChecksumValid());
        }

        [TestMethod]
        [TestCategory(Category)]
        public void HeldItems_DecodeToKnownValuesFromRealSave()
        {
            // Regression test for a stride bug: an earlier fix pass mistakenly widened each held
            // item slot from 23 bits (the real format, confirmed against src/items.c's
            // WriteItemSlotBits: flags(8) + quantity(7) + id(8), no padding) to 33 bits, which
            // "fixed" a save-time crash but silently corrupted every held item's data (each slot
            // decoded to nonsensical flag/quantity/id combinations). These are the known-good
            // first few slots from BRT.sav decoded at the correct 23-bit stride.
            var save = GetTestSave();

            Assert.IsTrue(save.HeldItems.Count >= 3);
            Assert.IsTrue(save.HeldItems[0].IsValid);
            Assert.AreEqual(33, save.HeldItems[0].ID);
            Assert.AreEqual(0, save.HeldItems[0].Parameter);
            Assert.IsTrue(save.HeldItems[2].IsValid);
            Assert.AreEqual(7, save.HeldItems[2].ID);
            Assert.AreEqual(3, save.HeldItems[2].Parameter);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void MarkBossRecruited_FlagBoss_AddsToRosterAndSetsCompleteFlag()
        {
            var save = GetTestSave();
            var originalCount = save.StoredPokemon.Count;

            var added = save.MarkBossRecruited(RBBossEncounters.Zapdos, CreateBossPokemon(RBBossEncounters.Zapdos, "Zapdos"));

            Assert.IsTrue(added);
            Assert.AreEqual(originalCount + 1, save.StoredPokemon.Count);
            Assert.IsTrue(save.StoredPokemon.Exists(p => p.ID == RBBossEncounters.Zapdos));
            Assert.IsTrue(save.ExclusivePokemonData.GetCutsceneFlag(RBCutsceneFlag.MtThunderPeakComplete));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void MarkBossRecruited_RosterOnlyBoss_AddsToRosterWithoutSettingAnyFlag()
        {
            var save = GetTestSave();
            var originalCount = save.StoredPokemon.Count;
            var flagsBefore = (bool[])save.ExclusivePokemonData.CutsceneFlags.Clone();

            var added = save.MarkBossRecruited(RBBossEncounters.Lugia, CreateBossPokemon(RBBossEncounters.Lugia, "Lugia"));

            Assert.IsTrue(added);
            Assert.AreEqual(originalCount + 1, save.StoredPokemon.Count);
            Assert.IsTrue(save.StoredPokemon.Exists(p => p.ID == RBBossEncounters.Lugia));
            CollectionAssert.AreEqual(flagsBefore, save.ExclusivePokemonData.CutsceneFlags, "Marking a roster-only boss (no mapped complete flag) should not change any cutscene flag.");
        }

        [TestMethod]
        [TestCategory(Category)]
        public void MarkBossRecruited_CalledTwice_DoesNotDuplicateRosterEntry()
        {
            var save = GetTestSave();
            var originalCount = save.StoredPokemon.Count;

            var firstCall = save.MarkBossRecruited(RBBossEncounters.Moltres, CreateBossPokemon(RBBossEncounters.Moltres, "Moltres"));
            var secondCall = save.MarkBossRecruited(RBBossEncounters.Moltres, CreateBossPokemon(RBBossEncounters.Moltres, "Moltres"));

            Assert.IsTrue(firstCall);
            Assert.IsFalse(secondCall);
            Assert.AreEqual(originalCount + 1, save.StoredPokemon.Count);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void MarkBossRecruited_SetsMonsterSeenFlag_LikeTheGamesOwnAddPaths()
        {
            // Mewtwo has never been fought or recruited on the fixture save, so its seen flag
            // starts false. Both of the game's roster-add paths set the seen flag on join;
            // PreSave enforces the same invariant for tool-added recruits.
            var save = GetTestSave();
            Assert.IsFalse(save.ExclusivePokemonData.MonsterSeenFlags[RBBossEncounters.Mewtwo]);

            save.MarkBossRecruited(RBBossEncounters.Mewtwo, CreateBossPokemon(RBBossEncounters.Mewtwo, "Mewtwo"));
            var reloaded = new RBSave(save.ToByteArray());

            Assert.IsTrue(reloaded.ExclusivePokemonData.MonsterSeenFlags[RBBossEncounters.Mewtwo]);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void MarkBossRecruited_RoundTripsThroughSaveBytes_WithValidChecksum()
        {
            var save = GetTestSave();
            save.MarkBossRecruited(RBBossEncounters.Rayquaza, CreateBossPokemon(RBBossEncounters.Rayquaza, "Rayquaza"));

            var savedBytes = save.ToByteArray();
            var reloaded = new RBSave(savedBytes);

            Assert.IsTrue(reloaded.IsPrimaryChecksumValid());
            Assert.IsTrue(reloaded.StoredPokemon.Exists(p => p.ID == RBBossEncounters.Rayquaza && p.Name == "Rayquaza"));
            Assert.IsTrue(reloaded.ExclusivePokemonData.GetCutsceneFlag(RBCutsceneFlag.SkyTowerSummitComplete), "The complete flag should persist through a real save/load cycle, not just in memory.");
        }

        [TestMethod]
        [TestCategory(Category)]
        public void FriendAreasUnlocked_HasExpectedLength()
        {
            var save = GetTestSave();
            Assert.AreEqual(58, save.FriendAreasUnlocked.Length);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void CanCurrentlyRecruit_False_UntilGatedBossFlagIsSet()
        {
            var save = GetTestSave();

            // The fixture save has already cleared Mt. Thunder, so simulate a pre-encounter
            // save by clearing the flag first.
            save.ExclusivePokemonData.SetCutsceneFlag(RBCutsceneFlag.MtThunderPeakComplete, false);
            Assert.IsFalse(save.CanCurrentlyRecruit(RBBossEncounters.Zapdos));

            save.ExclusivePokemonData.SetCutsceneFlag(RBCutsceneFlag.MtThunderPeakComplete, true);

            Assert.IsTrue(save.CanCurrentlyRecruit(RBBossEncounters.Zapdos));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void CanCurrentlyRecruit_True_ForBossNotGatedAtAll()
        {
            var save = GetTestSave();
            Assert.IsTrue(save.CanCurrentlyRecruit(RBBossEncounters.Lugia));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void CanCurrentlyRecruit_False_ForLatios_RegardlessOfFlagState()
        {
            var save = GetTestSave();

            // Latios is unconditionally excluded from combat recruiting by IsMonsterRecruitable in
            // the decomp -- not "not yet," never. Setting its cosmetic complete flag (which exists
            // only for the unrelated dialogue-selector mechanism) must not flip this to true.
            Assert.IsFalse(save.CanCurrentlyRecruit(RBBossEncounters.Latios));

            save.ExclusivePokemonData.SetCutsceneFlag(RBCutsceneFlag.NorthernRangeComplete, true);

            Assert.IsFalse(save.CanCurrentlyRecruit(RBBossEncounters.Latios));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void CanCurrentlyRecruit_False_ForLatias()
        {
            var save = GetTestSave();

            // Latias, like Latios, is unconditionally excluded from combat recruiting -- it has no
            // cutscene flag of its own to test against, since it's obtained through a story event.
            Assert.IsFalse(save.CanCurrentlyRecruit(RBBossEncounters.Latias));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void CanCurrentlyRecruit_ForARegi_IgnoresCutsceneFlag_ChecksItsOwnPartInStorage()
        {
            var save = GetTestSave();
            save.ExclusivePokemonData.SetCutsceneFlag(RBCutsceneFlag.RegiItemObtained, true);

            // The flag alone must NOT be enough -- it's transient scratch state the game
            // recomputes from current item possession every time any Regi's room is entered, not a
            // real persisted signal (see RBBossEncounters.RegiItems.PartIdsBySpecies's remarks).
            Assert.IsFalse(save.CanCurrentlyRecruit(RBBossEncounters.Regirock));

            save.StoredItems.Add(new RBStoredItem(RBBossEncounters.RegiItems.PartIdsBySpecies[RBBossEncounters.Regirock], 1));

            Assert.IsTrue(save.CanCurrentlyRecruit(RBBossEncounters.Regirock));
            // Regice's own Part wasn't added, so it should still be blocked -- each Regi's item
            // check is independent, not shared, despite them sharing one cutscene flag bit.
            Assert.IsFalse(save.CanCurrentlyRecruit(RBBossEncounters.Regice));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void CanCurrentlyRecruit_ForARegi_TrueWithMusicBoxInstead()
        {
            var save = GetTestSave();
            save.StoredItems.Add(new RBStoredItem(RBBossEncounters.RegiItems.MusicBoxItemId, 1));

            Assert.IsTrue(save.CanCurrentlyRecruit(RBBossEncounters.Regirock));
            Assert.IsTrue(save.CanCurrentlyRecruit(RBBossEncounters.Regice));
            Assert.IsTrue(save.CanCurrentlyRecruit(RBBossEncounters.Registeel));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void CanCurrentlyRecruit_ForARegi_TrueWithPartHeldInsteadOfStored()
        {
            var save = GetTestSave();
            save.HeldItems.Add(new RBHeldItem { ID = RBBossEncounters.RegiItems.PartIdsBySpecies[RBBossEncounters.Regice], Parameter = 0 });

            Assert.IsTrue(save.CanCurrentlyRecruit(RBBossEncounters.Regice));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void UnlockFriendArea_RoundTripsThroughSaveBytes_WithoutDisturbingOtherAreas()
        {
            var save = GetTestSave();
            var before = (bool[])save.FriendAreasUnlocked.Clone();

            var changed = save.UnlockFriendArea(RBFriendArea.ThunderMeadow);

            var savedBytes = save.ToByteArray();
            var reloaded = new RBSave(savedBytes);

            Assert.IsTrue(reloaded.IsPrimaryChecksumValid());
            Assert.IsTrue(changed || before[(int)RBFriendArea.ThunderMeadow], "Should report a change unless the area was already unlocked.");
            Assert.IsTrue(reloaded.FriendAreasUnlocked[(int)RBFriendArea.ThunderMeadow]);
            for (int i = 0; i < before.Length; i++)
            {
                if (i != (int)RBFriendArea.ThunderMeadow)
                {
                    Assert.AreEqual(before[i], reloaded.FriendAreasUnlocked[i], $"Friend area index {i} should be unchanged.");
                }
            }
        }

        [TestMethod]
        [TestCategory(Category)]
        public void ExclusivePokemonData_PlayTimeFieldsDecodeToPlausibleValues()
        {
            // Sanity check for RBOffsets.AdventureDataOffset itself: if the offset were wrong,
            // these would decode to near-random noise instead of values in valid ranges.
            var save = GetTestSave();

            // Not directly exposed on RBSave, so decode the same way LoadExclusivePokemonData's
            // neighboring PlayTime bits would, using the known field layout.
            var playTimeBitOffset = save.Offsets.AdventureDataOffset + save.Offsets.GameOptionsBitLength;
            var frames = save.Bits.GetInt(0, playTimeBitOffset, 6);
            var seconds = save.Bits.GetInt(0, playTimeBitOffset + 6, 6);
            var minutes = save.Bits.GetInt(0, playTimeBitOffset + 12, 6);

            Assert.IsTrue(frames is >= 0 and < 60);
            Assert.IsTrue(seconds is >= 0 and < 60);
            Assert.IsTrue(minutes is >= 0 and < 60);
        }
    }
}
