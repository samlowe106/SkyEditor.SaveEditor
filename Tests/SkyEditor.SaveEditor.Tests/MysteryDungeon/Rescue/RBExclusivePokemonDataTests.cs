using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    [TestClass]
    public class RBExclusivePokemonDataTests
    {
        private const string Category = "RB Exclusive Pokemon Data Tests";

        [TestMethod]
        [TestCategory(Category)]
        public void RoundTrip_PreservesAllFields()
        {
            var data = new RBExclusivePokemonData
            {
                Unknown0 = true
            };
            data.MonsterSeenFlags[0] = true;
            data.MonsterSeenFlags[145] = true; // Zapdos
            data.MonsterSeenFlags[RBExclusivePokemonData.MonsterSeenFlagCount - 1] = true;
            data.SetCutsceneFlag(RBCutsceneFlag.MtThunderPeakComplete, true);
            data.SetCutsceneFlag(RBCutsceneFlag.RegiRecruited, true);
            data.TutorialFlags[3] = true;
            data.ExclusivePokemonClaimed[11] = true;

            var roundTripped = new RBExclusivePokemonData(data.ToBitBlock());

            Assert.IsTrue(roundTripped.Unknown0);
            Assert.IsTrue(roundTripped.MonsterSeenFlags[0]);
            Assert.IsTrue(roundTripped.MonsterSeenFlags[145]);
            Assert.IsTrue(roundTripped.MonsterSeenFlags[RBExclusivePokemonData.MonsterSeenFlagCount - 1]);
            Assert.IsTrue(roundTripped.GetCutsceneFlag(RBCutsceneFlag.MtThunderPeakComplete));
            Assert.IsTrue(roundTripped.GetCutsceneFlag(RBCutsceneFlag.RegiRecruited));
            Assert.IsFalse(roundTripped.GetCutsceneFlag(RBCutsceneFlag.FrostyGrottoComplete));
            Assert.IsTrue(roundTripped.TutorialFlags[3]);
            Assert.IsTrue(roundTripped.ExclusivePokemonClaimed[11]);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void ToBitBlock_HasExpectedLength()
        {
            var data = new RBExclusivePokemonData();
            Assert.AreEqual(RBExclusivePokemonData.BitLength, data.ToBitBlock().Count);
            // 1 (unknown) + 424 (monster seen) + 64 (cutscene) + 31 (tutorial) + 12 (exclusive) = 532
            Assert.AreEqual(532, RBExclusivePokemonData.BitLength);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void MarkBossDefeated_SetsFlag_ForBossWithCompleteFlag()
        {
            var data = new RBExclusivePokemonData();

            var flagWasSet = data.MarkBossDefeated(RBBossEncounters.Zapdos);

            Assert.IsTrue(flagWasSet);
            Assert.IsTrue(data.GetCutsceneFlag(RBCutsceneFlag.MtThunderPeakComplete));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void MarkBossDefeated_NoOp_ForBossWithoutCompleteFlag()
        {
            var data = new RBExclusivePokemonData();

            var flagWasSet = data.MarkBossDefeated(RBBossEncounters.Lugia);

            Assert.IsFalse(flagWasSet);
            foreach (var flag in data.CutsceneFlags)
            {
                Assert.IsFalse(flag, "No cutscene flag should be set for a boss with no mapped complete flag.");
            }
        }

        [TestMethod]
        [TestCategory(Category)]
        public void HasCompletedFirstEncounter_False_UntilGatedBossFlagIsSet()
        {
            var data = new RBExclusivePokemonData();

            Assert.IsFalse(data.HasCompletedFirstEncounter(RBBossEncounters.Zapdos));

            data.SetCutsceneFlag(RBCutsceneFlag.MtThunderPeakComplete, true);

            Assert.IsTrue(data.HasCompletedFirstEncounter(RBBossEncounters.Zapdos));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void HasCompletedFirstEncounter_True_ForBossNotInFirstEncounterMap()
        {
            var data = new RBExclusivePokemonData();

            // Lugia relies on the roster check alone (see RBBossEncounters) -- nothing gates a
            // first encounter for it, so this should never report false regardless of flag state.
            Assert.IsTrue(data.HasCompletedFirstEncounter(RBBossEncounters.Lugia));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void HasCompletedFirstEncounter_True_ForARegi_RegardlessOfCutsceneFlagState()
        {
            var data = new RBExclusivePokemonData();

            // The Regis are deliberately absent from FirstEncounterFlagsByBoss (RegiItemObtained
            // is transient scratch state recomputed from current item possession on every Regi-room
            // entry, not a persisted "first encounter done" bit -- see RBSave.CanCurrentlyRecruit
            // for the actual, item-based check), so this must stay true unconditionally here.
            Assert.IsTrue(data.HasCompletedFirstEncounter(RBBossEncounters.Regirock));

            data.SetCutsceneFlag(RBCutsceneFlag.RegiItemObtained, false);
            Assert.IsTrue(data.HasCompletedFirstEncounter(RBBossEncounters.Regirock));
        }
    }
}
