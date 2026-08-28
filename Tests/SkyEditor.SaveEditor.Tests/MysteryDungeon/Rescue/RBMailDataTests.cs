using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    [TestClass]
    public class RBMailDataTests
    {
        private const string Category = "Mail Data Tests";

        private RBSave GetTestSave()
        {
            return new RBSave(DataUtil.GetBinaryResource("RRT.sav"));
        }

        // Golden fixture from the wondermail JS library's own test suite ("Help me" mission,
        // Tiny Woods 3F, Pikachu client, money reward). If this fails, the codec broke.
        private const string FixturePassword = "??JNS4+?4P6?2F?864?6P??W";

        [TestMethod]
        [TestCategory(Category)]
        public void Password_DecodesGoldenFixture()
        {
            var mail = RBWonderMailPassword.Decode(FixturePassword);

            Assert.IsNotNull(mail);
            Assert.AreEqual(RBWonderMail.MailTypeWonder, mail!.MailType);
            Assert.AreEqual(0, mail.MissionType);
            Assert.AreEqual(0, mail.DungeonId);
            Assert.AreEqual(3, mail.Floor);
            Assert.AreEqual(25, mail.ClientSpecies);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void Password_RoundTripsAndNormalizesInput()
        {
            var mail = RBWonderMailPassword.Decode(FixturePassword);
            Assert.AreEqual(FixturePassword, RBWonderMailPassword.Encode(mail!));

            // Lowercase, grouped, and glyph-spelled input should all decode identically.
            var sloppy = "??jn s4+? 4p6? 2f?8 64?6 p??w";
            Assert.AreEqual(FixturePassword, RBWonderMailPassword.Encode(RBWonderMailPassword.Decode(sloppy)!));
            var glyphs = RBWonderMailPassword.FormatForDisplay("??-.S4T?4RN?XF?664?R%??W");
            Assert.AreEqual("??-.S4T?4RN?XF?664?R%??W", RBWonderMailPassword.Encode(RBWonderMailPassword.Decode(glyphs)!));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void Password_RejectsBadChecksumAndBadInput()
        {
            Assert.IsNull(RBWonderMailPassword.Decode("??JNS4+?4P6?2F?864?6P??F")); // last char corrupted
            Assert.IsNull(RBWonderMailPassword.Decode("too short"));
            Assert.IsNull(RBWonderMailPassword.Decode(null));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void MailData_DecodesRealSave()
        {
            var mail = GetTestSave().MailData;

            Assert.AreEqual(2, mail.MailboxSlots.Count(m => !m.IsEmpty));
            Assert.AreEqual(6, mail.PelipperBoardJobs.Count(m => !m.IsEmpty));
            Assert.AreEqual(3, mail.JobSlots.Count(m => !m.IsEmpty));
            Assert.AreEqual(16, mail.UsedMailHistory.Count(r => !r.IsEmpty));

            // First accepted job on this save: a "Help me" job for Manectric (RB id 335) on
            // Frosty Forest (dungeon 11) 6F. Its password was verified against an independent
            // decode of the raw bytes.
            var job = mail.JobSlots[0];
            Assert.IsTrue(job.IsWonderMail);
            Assert.AreEqual(0, job.MissionType);
            Assert.AreEqual(335, job.ClientSpecies);
            Assert.AreEqual(11, job.DungeonId);
            Assert.AreEqual(6, job.Floor);
            Assert.AreEqual(0x57720C, job.Seed);
            Assert.AreEqual("??7T9KS?4+NPQQ?C4676+N?.", RBWonderMailPassword.Encode(job));

            // Accepting a board job copies it verbatim; this save still has all three accepted
            // jobs' board originals on the board.
            Assert.IsTrue(mail.PelipperBoardJobs.Any(b => RBWonderMailPassword.Encode(b) == "??7T9KS?4+NPQQ?C4676+N?."));

            // Newest and oldest used-password history entries.
            Assert.AreEqual(0x0892FE82u, mail.UsedMailHistory[0].Checksum);
            Assert.AreEqual(0xFF500E, mail.UsedMailHistory[0].Seed);
            Assert.AreEqual(46, mail.UsedMailHistory[0].DungeonId);
            Assert.AreEqual(4, mail.UsedMailHistory[0].Floor);
            Assert.AreEqual(0x0894F265u, mail.UsedMailHistory[15].Checksum);
            Assert.AreEqual(43, mail.UsedMailHistory[15].DungeonId);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void MailData_RoundTripsThroughSaveBytesUnchanged()
        {
            var save = GetTestSave();
            var original = save.ToByteArray();
            var reloaded = new RBSave(original);

            Assert.IsTrue(reloaded.IsPrimaryChecksumValid());
            var expected = save.MailData.ToBitBlock();
            var actual = reloaded.MailData.ToBitBlock();
            Assert.IsTrue(expected.SequenceEqual(actual), "Mail block should survive an unmodified save/load cycle bit-for-bit.");
        }

        [TestMethod]
        [TestCategory(Category)]
        public void RemoveJob_CompactsAndPersists()
        {
            var save = GetTestSave();
            var lastJobPassword = RBWonderMailPassword.Encode(save.MailData.JobSlots[2]);

            save.MailData.RemoveJob(1);

            var reloaded = new RBSave(save.ToByteArray());
            Assert.IsTrue(reloaded.IsPrimaryChecksumValid());
            Assert.AreEqual(2, reloaded.MailData.JobSlots.Count(m => !m.IsEmpty));
            // The job that was in slot 2 compacts down into slot 1.
            Assert.AreEqual(lastJobPassword, RBWonderMailPassword.Encode(reloaded.MailData.JobSlots[1]));
            // The freed slot carries the game's own reset pattern.
            var freed = reloaded.MailData.JobSlots[2];
            Assert.IsTrue(freed.IsEmpty);
            Assert.AreEqual(RBWonderMail.EmptyDungeonId, freed.DungeonId);
            Assert.AreEqual(5, freed.RewardType);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void RemoveUsedMailRecord_ShiftsHistoryAndFreesPassword()
        {
            var save = GetTestSave();
            var second = save.MailData.UsedMailHistory[1];

            save.MailData.RemoveUsedMailRecord(0);

            var reloaded = new RBSave(save.ToByteArray());
            Assert.IsTrue(reloaded.IsPrimaryChecksumValid());
            Assert.AreEqual(15, reloaded.MailData.UsedMailHistory.Count(r => !r.IsEmpty));
            Assert.AreEqual(second.Checksum, reloaded.MailData.UsedMailHistory[0].Checksum);
            var tail = reloaded.MailData.UsedMailHistory[15];
            Assert.IsTrue(tail.IsEmpty);
            Assert.AreEqual(RBUsedWonderMailRecord.EmptyDungeonId, tail.DungeonId);
            Assert.AreEqual(1, tail.Floor);
        }
    }
}
