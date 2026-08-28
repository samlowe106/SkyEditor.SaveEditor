using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkyEditor.SaveEditor.MysteryDungeon.Rescue;

namespace SkyEditor.SaveEditor.Tests.MysteryDungeon.Rescue
{
    /// <summary>
    /// Tests for <see cref="RBSave.FromFile"/> against a save wrapped in the SharkPortSave (.sps)
    /// container format used by GameShark/Action Replay-era save exports (e.g. GameFAQs' "Saves" downloads).
    /// </summary>
    [TestClass]
    public class SharkPortImportTests
    {
        private const string Category = "SharkPort Import Tests";

        /// <summary>
        /// Wraps a real save's bytes in a synthetic-but-structurally-real SharkPortSave header:
        /// magic, version, then three length-prefixed strings (title, date, notes), followed
        /// immediately by the save payload. Exercises the real container format without bundling
        /// a third-party downloaded save file as a test resource.
        /// </summary>
        private static byte[] WrapInSharkPort(byte[] saveBytes)
        {
            using var stream = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(stream);

            void WriteLengthPrefixed(string s)
            {
                var bytes = Encoding.ASCII.GetBytes(s);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }

            var magic = Encoding.ASCII.GetBytes("SharkPortSave");
            writer.Write(magic.Length);
            writer.Write(magic);
            writer.Write(0x000f0000);
            WriteLengthPrefixed("POKE DUNGEON");
            WriteLengthPrefixed("1/1/2006 00:00:00");
            WriteLengthPrefixed("");
            writer.Write(saveBytes);

            return stream.ToArray();
        }

        private static byte[] GetRealSaveBytes() => DataUtil.GetBinaryResource("RRT.sav");

        [TestMethod]
        [TestCategory(Category)]
        public void IsSharkPortFormat_TrueForWrappedSave_FalseForRawSave()
        {
            var wrapped = WrapInSharkPort(GetRealSaveBytes());
            Assert.IsTrue(SharkPortFile.IsSharkPortFormat(wrapped));
            Assert.IsFalse(SharkPortFile.IsSharkPortFormat(GetRealSaveBytes()));
        }

        [TestMethod]
        [TestCategory(Category)]
        public void FromFile_OnWrappedSave_ExtractsAndParsesTheRealSave()
        {
            var wrapped = WrapInSharkPort(GetRealSaveBytes());

            var save = RBSave.FromFile(wrapped);

            Assert.IsNotNull(save);
            Assert.IsTrue(save!.IsPrimaryChecksumValid());
            Assert.IsTrue(save.IsSecondaryChecksumValid());
            Assert.AreEqual("Pokémon", save.TeamName);
            Assert.AreEqual(20, save.StoredPokemon.Count);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void FromFile_OnRawSave_ParsesDirectlyWithoutSharkPortHandling()
        {
            var save = RBSave.FromFile(GetRealSaveBytes());

            Assert.IsNotNull(save);
            Assert.AreEqual("Pokémon", save!.TeamName);
        }

        [TestMethod]
        [TestCategory(Category)]
        public void FromFile_OnSharkPortContainerWithNoValidPayload_ReturnsNull()
        {
            var wrapped = WrapInSharkPort(Enumerable.Repeat((byte)0xFF, RBSave.RawFileLength).ToArray());

            var save = RBSave.FromFile(wrapped);

            Assert.IsNull(save);
        }
    }
}
