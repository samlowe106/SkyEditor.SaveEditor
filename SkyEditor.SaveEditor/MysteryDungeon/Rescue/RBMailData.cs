using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    /// <summary>
    /// One entry of the used-Wonder-Mail history: the fingerprint the game keeps of a completed
    /// Wonder Mail job so its password can't be entered again.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>subStruct_203B490</c> (include/code_80958E8.h): a checksum over the mail's
    /// fields (CalculateMailChecksum) plus the mission's dungeon seed and location. Pushed as a
    /// 16-entry FIFO when a job completes (sub_8096EEC, called from the thank-you scene); a
    /// password matching any entry is rejected as "already received" (sub_8096F50). Because it's
    /// a FIFO, completing 16 other jobs makes an old password usable again.
    /// </remarks>
    public class RBUsedWonderMailRecord
    {
        public const int BitLength = 32 + 24 + 14;

        /// <summary>The dungeon id InitializeMailJobsNews writes into empty entries (no real dungeon).</summary>
        public const int EmptyDungeonId = 99;

        public uint Checksum { get; set; }
        public int Seed { get; set; }
        public int DungeonId { get; set; }
        public int Floor { get; set; }

        /// <summary>Matches the exact empty pattern InitializeMailJobsNews writes (id 99, floor 1, zeros).</summary>
        public bool IsEmpty => Checksum == 0 && Seed == 0 && DungeonId == EmptyDungeonId && Floor == 1;

        public RBUsedWonderMailRecord()
        {
        }

        public RBUsedWonderMailRecord(BitBlock bits)
        {
            bits.Position = 0;
            Checksum = bits.GetNextUInt(32);
            Seed = bits.GetNextInt(24);
            DungeonId = bits.GetNextInt(7);
            Floor = bits.GetNextInt(7);
        }

        public BitBlock ToBitBlock()
        {
            var bits = new BitBlock(BitLength) { Position = 0 };
            bits.SetNextUInt(32, Checksum);
            bits.SetNextInt(24, Seed);
            bits.SetNextInt(7, DungeonId);
            bits.SetNextInt(7, Floor);
            return bits;
        }

        public static RBUsedWonderMailRecord CreateEmpty() => new RBUsedWonderMailRecord
        {
            DungeonId = EmptyDungeonId,
            Floor = 1,
        };

        /// <summary>e.g. "Fantasy Strait 4F (seed ff500e)". The full mail isn't recoverable from a fingerprint.</summary>
        public string GetSummary() => $"{RBWonderMail.DungeonName(DungeonId)} {Floor}F (seed {Seed:x6})";
    }

    /// <summary>
    /// The save's mail block: mailbox, Pelipper board jobs, accepted jobs, Pokemon News, and
    /// the used-Wonder-Mail history.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>unkStruct_203B490</c> and its serialization <c>SaveMailInfo</c>/<c>RestoreMailInfo</c>
    /// (src/code_80958E8.c): 4 mailbox slots + 8 Pelipper board jobs + 8 accepted job slots
    /// (93 bits each), 56 Pokemon News bits + 1 unknown bit, two unknown regions (40 + 120
    /// bytes, preserved verbatim here), then the 16 used-mail history entries (70 bits each).
    /// 4317 bits total, in a 0x221-byte reserved buffer. Loaded from
    /// <see cref="RBSave.RBOffsets.MailDataOffset"/>.
    ///
    /// Removal helpers mirror the game's own routines: slots are reset with the exact field
    /// pattern ResetMailboxSlot/ResetPelipperBoardSlot/ResetJobSlot write, then compacted toward
    /// index 0 the way ShiftMailboxSlotsDown/ShiftPelipperJobsDown/ShiftJobSlotsDown do, so a
    /// tool-edited block is indistinguishable from one the game produced itself.
    /// </remarks>
    public class RBMailData
    {
        public const int MailboxSlotCount = 4;
        public const int JobSlotCount = 8; // MAX_ACCEPTED_JOBS, also the Pelipper board size
        public const int NewsCount = 56;   // NUM_POKEMON_NEWS
        public const int UsedMailHistoryCount = 16;
        private const int UnknownRegionBitLength = 40 * 8 + 120 * 8;

        public const int BitLength =
            (MailboxSlotCount + JobSlotCount + JobSlotCount) * RBWonderMail.BitLength
            + NewsCount + 1 + UnknownRegionBitLength
            + UsedMailHistoryCount * RBUsedWonderMailRecord.BitLength;

        /// <summary>rewardType the game's reset writes for mailbox and accepted-job slots (MONEY1).</summary>
        private const int EmptyRewardTypeMailboxAndJobs = 5;
        private const int EmptyRewardTypePelipperBoard = 0;

        /// <summary>Friend-rescue mail (SOS/A-OK/Thank-You) received in the mailbox.</summary>
        public List<RBWonderMail> MailboxSlots { get; } = new List<RBWonderMail>();

        /// <summary>The Pelipper Post Office bulletin board's current job postings.</summary>
        public List<RBWonderMail> PelipperBoardJobs { get; } = new List<RBWonderMail>();

        /// <summary>Jobs the player has accepted (from the board or from Wonder Mail passwords).</summary>
        public List<RBWonderMail> JobSlots { get; } = new List<RBWonderMail>();

        public bool[] NewsReceived { get; } = new bool[NewsCount];

        public bool Unknown328 { get; set; }

        private BitBlock unknownRegion = new BitBlock(UnknownRegionBitLength);

        /// <summary>Completed-job fingerprints, newest first; see <see cref="RBUsedWonderMailRecord"/>.</summary>
        public List<RBUsedWonderMailRecord> UsedMailHistory { get; } = new List<RBUsedWonderMailRecord>();

        public RBMailData()
        {
            for (int i = 0; i < MailboxSlotCount; i++) MailboxSlots.Add(RBWonderMail.CreateEmptySlot(EmptyRewardTypeMailboxAndJobs));
            for (int i = 0; i < JobSlotCount; i++) PelipperBoardJobs.Add(RBWonderMail.CreateEmptySlot(EmptyRewardTypePelipperBoard));
            for (int i = 0; i < JobSlotCount; i++) JobSlots.Add(RBWonderMail.CreateEmptySlot(EmptyRewardTypeMailboxAndJobs));
            for (int i = 0; i < UsedMailHistoryCount; i++) UsedMailHistory.Add(RBUsedWonderMailRecord.CreateEmpty());
        }

        public RBMailData(BitBlock bits)
        {
            var position = 0;
            BitBlock Next(int length)
            {
                var range = bits.GetRange(position, length);
                position += length;
                return range;
            }

            for (int i = 0; i < MailboxSlotCount; i++) MailboxSlots.Add(new RBWonderMail(Next(RBWonderMail.BitLength)));
            for (int i = 0; i < JobSlotCount; i++) PelipperBoardJobs.Add(new RBWonderMail(Next(RBWonderMail.BitLength)));
            for (int i = 0; i < JobSlotCount; i++) JobSlots.Add(new RBWonderMail(Next(RBWonderMail.BitLength)));
            for (int i = 0; i < NewsCount; i++) NewsReceived[i] = bits[position + i];
            position += NewsCount;
            Unknown328 = bits[position];
            position += 1;
            unknownRegion = Next(UnknownRegionBitLength);
            for (int i = 0; i < UsedMailHistoryCount; i++) UsedMailHistory.Add(new RBUsedWonderMailRecord(Next(RBUsedWonderMailRecord.BitLength)));
        }

        public BitBlock ToBitBlock()
        {
            var bits = new BitBlock(BitLength);
            var position = 0;
            void Put(BitBlock block)
            {
                bits.SetRange(position, block.Count, block);
                position += block.Count;
            }

            foreach (var mail in MailboxSlots) Put(mail.ToBitBlock());
            foreach (var mail in PelipperBoardJobs) Put(mail.ToBitBlock());
            foreach (var mail in JobSlots) Put(mail.ToBitBlock());
            for (int i = 0; i < NewsCount; i++) bits[position + i] = NewsReceived[i];
            position += NewsCount;
            bits[position] = Unknown328;
            position += 1;
            Put(unknownRegion);
            foreach (var record in UsedMailHistory) Put(record.ToBitBlock());
            return bits;
        }

        public void RemoveMailboxSlot(int index) => RemoveAndCompact(MailboxSlots, index, EmptyRewardTypeMailboxAndJobs);

        public void RemovePelipperBoardJob(int index) => RemoveAndCompact(PelipperBoardJobs, index, EmptyRewardTypePelipperBoard);

        public void RemoveJob(int index) => RemoveAndCompact(JobSlots, index, EmptyRewardTypeMailboxAndJobs);

        /// <summary>
        /// Removes one used-mail fingerprint, making its Wonder Mail password enterable again.
        /// Later (older) entries shift up and an empty entry fills the tail, exactly the state
        /// the FIFO would be in had this job never completed.
        /// </summary>
        public void RemoveUsedMailRecord(int index)
        {
            UsedMailHistory.RemoveAt(index);
            UsedMailHistory.Add(RBUsedWonderMailRecord.CreateEmpty());
        }

        private static void RemoveAndCompact(List<RBWonderMail> slots, int index, int emptyRewardType)
        {
            slots.RemoveAt(index);
            slots.Add(RBWonderMail.CreateEmptySlot(emptyRewardType));

            // Compact non-empty entries toward index 0 (the game's ShiftJobSlotsDown behavior),
            // in case the block already contained holes.
            var occupied = slots.Where(m => !m.IsEmpty).ToList();
            for (int i = 0; i < slots.Count; i++)
            {
                slots[i] = i < occupied.Count ? occupied[i] : RBWonderMail.CreateEmptySlot(emptyRewardType);
            }
        }
    }
}
