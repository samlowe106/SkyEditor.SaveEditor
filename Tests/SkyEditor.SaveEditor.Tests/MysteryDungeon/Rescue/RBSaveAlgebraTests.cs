using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    /// <summary>
    /// Algebraic properties of the load/edit/save pipeline, tested at the byte level against a
    /// real, human-played save. The mental model: serialization is a pure function of the
    /// in-memory model, edits are last-writer-wins field writes, and the only deliberate
    /// impurities are two monotone "ratchets" that mirror the game's own irreversible
    /// bookkeeping (the Adventure Log's numJoined counter and the monster seen / ever-recruited
    /// flags). Everything else must satisfy:
    ///   identity      -- save(load(b)) == b for an organic save b
    ///   idempotence   -- resaving an already-saved file changes nothing
    ///   invertibility -- edit, save, revert, save returns the original bytes exactly
    ///   commutativity -- edits to disjoint fields produce the same bytes in either order
    /// Failures here are exactly the "telltale signs of manipulation" class of bug: bytes the
    /// game would have preserved but the tool silently normalized.
    /// </summary>
    [TestClass]
    public class RBSaveAlgebraTests
    {
        private const string Category = "RB Save Algebra Tests";

        private static byte[] Original => DataUtil.GetBinaryResource("RRT.sav");

        private static byte[] Apply(byte[] start, Action<RBSave> edit)
        {
            var save = new RBSave(start);
            edit(save);
            return save.ToByteArray();
        }

        private static void AssertBytesEqual(byte[] expected, byte[] actual, string context)
        {
            Assert.AreEqual(expected.Length, actual.Length, context);
            var diffs = new List<int>();
            for (int i = 0; i < expected.Length && diffs.Count < 8; i++)
            {
                if (expected[i] != actual[i])
                {
                    diffs.Add(i);
                }
            }
            Assert.AreEqual(0, diffs.Count,
                $"{context}: bytes differ at [{string.Join(", ", diffs.Select(d => $"0x{d:X5}"))}]{(diffs.Count == 8 ? "..." : "")}");
        }

        [TestMethod]
        [TestCategory(Category)]
        public void Identity_ResaveWithoutEdits_IsByteIdentical()
        {
            // The strongest possible no-fingerprint guarantee: loading an organic save and
            // saving it untouched reproduces every one of the 131072 bytes, including checksum,
            // backup block, stale held-item quantity bits on itemless Pokemon, and name-buffer
            // garbage past the terminator. This test caught both of those normalization bugs.
            AssertBytesEqual(Original, Apply(Original, _ => { }), "no-op resave");
        }

        [TestMethod]
        [TestCategory(Category)]
        public void Idempotence_ResavingAnEditedSave_ChangesNothingFurther()
        {
            var once = Apply(Original, s => s.HeldMoney = 500);
            var twice = Apply(once, _ => { });
            AssertBytesEqual(once, twice, "second save of an already-saved edit");
        }

        [TestMethod]
        [TestCategory(Category)]
        public void Invertibility_EditSaveRevertSave_ReturnsTheOriginalBytes()
        {
            var original = Original;
            var probe = new RBSave(original);
            var lockedArea = Array.FindIndex(probe.FriendAreasUnlocked, 1, unlocked => !unlocked);
            Assert.IsTrue(lockedArea > 0, "Need at least one locked friend area for this test.");
            var toolboxItem = (RBHeldItem)probe.HeldItems[1].Clone();
            var storedQty = probe.StoredItems.Find(i => i.ItemID == toolboxItem.ID)?.Quantity;

            var cases = new (string Name, Action<RBSave> Edit, Action<RBSave> Revert)[]
            {
                ("held money", s => s.HeldMoney += 1000, s => s.HeldMoney -= 1000),
                ("stored money", s => s.StoredMoney = 0, s => s.StoredMoney = 290),
                ("cutscene flag", s => s.ExclusivePokemonData.SetCutsceneFlag(RBCutsceneFlag.HowlingForestComplete, false),
                                  s => s.ExclusivePokemonData.SetCutsceneFlag(RBCutsceneFlag.HowlingForestComplete, true)),
                ("friend area", s => s.FriendAreasUnlocked[lockedArea] = true,
                                s => s.FriendAreasUnlocked[lockedArea] = false),
                // Held item id only: the quantity bits hold organic garbage (61 on this slot)
                // that must ride along untouched for the revert to be exact.
                ("held item", s => s.StoredPokemon.Find(p => p.SlotIndex == 55)!.HeldItemId = 55,
                              s => s.StoredPokemon.Find(p => p.SlotIndex == 55)!.HeldItemId = 0),
                ("toolbox slot", s => s.HeldItems.RemoveAt(1),
                                 s => s.HeldItems.Insert(1, (RBHeldItem)toolboxItem.Clone())),
            };

            foreach (var (name, edit, revert) in cases)
            {
                var afterEdit = Apply(original, edit);
                CollectionAssert.AreNotEqual(original, afterEdit, $"{name}: the edit should actually change the file");
                var afterRevert = Apply(afterEdit, revert);
                AssertBytesEqual(original, afterRevert, $"{name}: edit-save-revert-save");
            }
        }

        [TestMethod]
        [TestCategory(Category)]
        public void Commutativity_DisjointEdits_ProduceTheSameBytesInAnyOrder()
        {
            var edits = new Action<RBSave>[]
            {
                s => s.HeldMoney = 12345,
                s => s.ExclusivePokemonData.SetCutsceneFlag(RBCutsceneFlag.HowlingForestComplete, false),
                s => s.StoredPokemon.Find(p => p.SlotIndex == 55)!.HeldItemId = 55,
            };

            var forward = Apply(Original, s => { foreach (var e in edits) e(s); });
            var backward = Apply(Original, s => { foreach (var e in edits.Reverse()) e(s); });
            AssertBytesEqual(forward, backward, "disjoint edits applied in opposite orders");
        }

        [TestMethod]
        [TestCategory(Category)]
        public void Ratchets_RosterAddThenRemoveAcrossSaves_ChangesOnlyTheDocumentedCounters()
        {
            // Recruit Mewtwo (never recruited or seen on this save), save, reload, delete it,
            // save again. The roster bytes must return to their original state exactly; the only
            // surviving differences are the game's own irreversible bookkeeping, which the tool
            // deliberately mimics: numJoined ticks up, Mewtwo's seen flag and ever-recruited
            // (unk1C) flag latch on, and the friend area owning the slot he landed in latches
            // unlocked (roster-implies-area-unlocked, mirroring the game's recruit-requires-camp
            // rule). Anything outside that byte set is an unintended fingerprint.
            const int Mewtwo = 150;
            var original = Original;

            var added = Apply(original, s => s.StoredPokemon.Add(new RBStoredPokemon
            {
                ID = Mewtwo,
                Name = "Mewtwo",
                Level = 70,
                HP = 1,
                Attack1 = new RBAttack(),
                Attack2 = new RBAttack(),
                Attack3 = new RBAttack(),
                Attack4 = new RBAttack(),
            }));
            int landedSlot = -1;
            var removed = Apply(added, s =>
            {
                var mewtwo = s.StoredPokemon.Find(p => p.ID == Mewtwo)!;
                landedSlot = mewtwo.SlotIndex;
                s.StoredPokemon.Remove(mewtwo);
            });
            var landedArea = RBFriendAreaCapacity.AreaForSlot(landedSlot);

            var check = new RBSave(removed);
            Assert.IsTrue(check.IsPrimaryChecksumValid());
            Assert.AreEqual(20, check.StoredPokemon.Count, "The roster itself should be back to its original 20 members.");
            Assert.AreEqual(19, check.NumPokemonRecruited, "numJoined is an increment-only counter, exactly like the game's.");
            Assert.IsTrue(check.ExclusivePokemonData.MonsterSeenFlags[Mewtwo], "Seen flags are never recomputed by the game, so the tool must leave the latch set.");
            Assert.IsTrue(check.HasRecruitedSpeciesFlag(Mewtwo));
            Assert.IsTrue(check.FriendAreasUnlocked[(int)landedArea], "The area owning the occupied slot must have latched unlocked.");

            // Every differing byte must be inside a documented ratchet field (or a checksum, or
            // the backup mirror of one of those).
            var offsets = check.Offsets;
            var allowed = new HashSet<int> { 0, 1, 2, 3 };
            void AllowBits(int bitStart, int bitCount)
            {
                for (int b = bitStart; b < bitStart + bitCount; b++)
                {
                    allowed.Add(b / 8);
                }
            }
            AllowBits(offsets.NumPokemonRecruitedOffset, offsets.NumPokemonRecruitedBitLength);
            AllowBits(offsets.RecruitedSpeciesFlagsOffset + Mewtwo, 1);
            AllowBits(offsets.ExclusivePokemonDataOffset + 1 + Mewtwo, 1); // unk0 bit, then seen flags
            AllowBits(offsets.FriendAreaOffset + (int)landedArea, 1);
            foreach (var b in allowed.ToArray())
            {
                allowed.Add(b + 0x6000);
            }

            var unexpected = new List<int>();
            for (int i = 0; i < original.Length; i++)
            {
                if (original[i] != removed[i] && !allowed.Contains(i))
                {
                    unexpected.Add(i);
                }
            }
            Assert.AreEqual(0, unexpected.Count,
                $"Bytes outside the documented ratchets changed: [{string.Join(", ", unexpected.Take(8).Select(d => $"0x{d:X5}"))}]");
        }
    }
}
