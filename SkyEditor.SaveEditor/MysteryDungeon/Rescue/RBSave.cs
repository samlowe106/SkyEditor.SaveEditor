using SkyEditor.Core.IO;
using SkyEditor.IO.FileSystem;
using SkyEditor.SaveEditor.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    public class RBSave : BitBlockFile
    {
        public class RBOffsets
        {
            // Checksums
            public virtual int ChecksumEnd => 0x57D0;
            public virtual int BackupSaveStart => 0x6000;

            // General
            public virtual int TeamNameStart => 0x4EC8 * 8;
            public virtual int TeamNameLength => 10;
            public virtual int BaseTypeOffset => 0x67 * 8;
            public virtual int HeldMoneyOffset => 0x4E6C * 8;
            public virtual int HeldMoneyLength => 24;
            public virtual int StoredMoneyOffset => 0x4E6F * 8;
            public virtual int StoredMoneyLength => 24;
            public virtual int RescuePointsOffset => 0x4ED3 * 8;
            public virtual int RescuePointsLength => 32;

            // Stored Items
            public virtual int StoredItemOffset => 0x4D2B * 8 - 2;
            public virtual int StoredItemCount => 239;

            // Held Items
            public virtual int HeldItemOffset => 0x4CF0 * 8;
            public virtual int HeldItemCount => 20;
            public virtual int HeldItemLength => 23;

            // Stored Pokemon
            public virtual int StoredPokemonOffset => (0x5B3 * 8 + 3) - (323 * 9);
            public virtual int StoredPokemonLength => 323;
            public virtual int StoredPokemonCount => 407 + 6;

            // Adventure Data (GameOptions + PlayTime + AdventureBits + ExclusivePokemonData,
            // written as one contiguous bitstream by SaveAdventureData/RestoreAdventureData
            // in the pret/pmd-red decomp). Offset and bit widths verified empirically against
            // a real save file: at this offset, PlayTime/GameOptions/AdventureBits all decode
            // to plausible values (small hours/minutes/seconds, small counters, etc.) rather
            // than noise.
            // Friend Areas (which friend areas have been unlocked). Sits immediately before
            // AdventureDataOffset -- derived the same way: RescueTeamInfo (16 bytes, containing
            // the already-verified TeamNameStart) ends at 0x4ED8, this section is 8 bytes
            // (FRIEND_AREA_COUNT=58 bits, one per area, out of a 64-bit buffer) written right
            // after it by SaveFriendAreas() in the decomp, and 0x4ED8 + 8 == 0x4EE0 lands exactly
            // on the independently-verified AdventureDataOffset -- so this offset is bracketed by
            // two already-confirmed real offsets, not just decomp arithmetic alone.
            public virtual int FriendAreaOffset => 0x4ED8 * 8;
            public virtual int FriendAreaCount => 58;

            public virtual int AdventureDataOffset => 0x4EE0 * 8;
            public virtual int GameOptionsBitLength => 14;
            public virtual int PlayTimeBitLength => 32;

            // AdventureBits (struct unkStruct_203B494 / "gUnknown_203B494" in the decomp,
            // src/adventure_info.c's WriteAdventureBits/ReadAdventureBits) is the Adventure
            // Log's backing data: numAdventures(17) + friendRescueSuccesses(17) +
            // numEvolved(17) + achievements(32) + thievingSuccesses(14) + numJoined(14) +
            // adventureMovesLearned(9) + numFloorsExplored(17) + unk1C[14](448) +
            // unk54[14](448) + learnedMoves[13](416) + dungeonLocation(7+7, the trailing
            // WriteDungeonLocationBits call in WriteAdventureBits) = 1463 bits.
            //
            // The dungeonLocation field is easy to miss when tallying WriteAdventureBits: it was
            // once dropped from this count (giving 1449), which shifted every ExclusivePokemonData
            // read/write 14 bits early and made cutscene flag N read as flag N-14 (and the first
            // 14 monster-seen bits read as dungeonLocation). 1463 is the verified value: at this
            // width, on a real mid-game save, all 20 roster species read seen, cutscene flags form
            // a contiguous story-order run, and the 12 ExclusivePokemonClaimed bits decode to
            // exactly the RED/BLUE_EXCLUSIVE in_rrt pattern (1,0,0,0,1,1,1,0,0,1,1,0) that
            // InitializeExclusivePokemon writes on every new Red Rescue Team game. Note the
            // numJoined-decodes-to-18 check anchors only AdventureDataOffset (the start), and says
            // nothing about this width; don't use it to justify changing this value.
            public virtual int AdventureBitsBitLength => 1463;
            public virtual int ExclusivePokemonDataOffset => AdventureDataOffset + GameOptionsBitLength + PlayTimeBitLength + AdventureBitsBitLength;

            // Fields inside AdventureBits needed to keep the Adventure Log in sync when adding
            // roster entries directly (RecruitFromGuide/MarkBossRecruited bypass the game's own
            // TryRecruitMonster/IncrementAdventureNumJoined, which normally do this).
            // Mail block (mailbox + Pelipper board + accepted jobs + Pokemon News + the
            // used-Wonder-Mail history FIFO), written by SaveMailInfo (src/code_80958E8.c) into
            // a 0x221-byte buffer. Offset derived by walking save.c's restore chain forward from
            // the verified AdventureDataOffset: 0x4EE0 + 0x100 (AdventureData) + 0x594
            // (sub_80954CC) = 0x5574; 0x5574 + 0x221 = 0x5795, inside the checksummed region
            // (ChecksumEnd 0x57D0). Verified empirically on a real save: this offset decodes
            // 2 mailbox mails + 6 Pelipper board jobs + 3 accepted jobs (byte-identical copies
            // of 3 of the board jobs, exactly how accepting a board job works), empty slots
            // carrying the exact ResetJobSlot/ResetMailboxSlot sentinel (dungeon id 99), and a
            // full 16-entry used-mail history.
            public virtual int MailDataOffset => 0x5574 * 8;

            public virtual int NumPokemonRecruitedOffset => AdventureDataOffset + GameOptionsBitLength + PlayTimeBitLength + 17 + 17 + 17 + 32 + 14;
            public virtual int NumPokemonRecruitedBitLength => 14;
            public virtual int RecruitedSpeciesFlagsOffset => NumPokemonRecruitedOffset + NumPokemonRecruitedBitLength + 9 + 17;
            public virtual int RecruitedSpeciesFlagsBitLength => 14 * 32;
            public virtual int RecruitedSpeciesCount => 424; // MONSTER_MAX (MONSTER_RAYQUAZA_CUTSCENE + 1)
        }

        public RBSave()
        {
            Offsets = new RBOffsets();
        }

        public RBSave(IEnumerable<byte> rawData) : base(rawData)
        {
            Offsets = new RBOffsets();
            Init();
        }

        /// <summary>Physical save media size: a 1Mbit (128KB) flash chip.</summary>
        public const int RawFileLength = 131072;

        public RBOffsets Offsets { get; set; }

        /// <summary>
        /// Parses a save that may be wrapped in a SharkPortSave (.sps) container, such as the
        /// files GameFAQs distributes under its "Saves" section. Returns a normal <see cref="RBSave"/>
        /// either way -- if <paramref name="rawData"/> isn't SharkPort-wrapped, it's parsed directly.
        /// </summary>
        /// <returns>The parsed save, or null if this is a SharkPort container but no valid save payload could be located inside it.</returns>
        public static RBSave FromFile(byte[] rawData)
        {
            if (!SharkPortFile.IsSharkPortFormat(rawData))
            {
                return new RBSave(rawData);
            }

            var offsets = new RBOffsets();
            var payload = SharkPortFile.ExtractPayload(rawData, RawFileLength, candidate =>
            {
                // Deliberately checks the checksum directly against a bare BitBlock rather than
                // constructing a full RBSave (which would fully parse the roster, items, etc. for
                // every candidate offset tried) -- most candidate offsets are garbage, and decoding
                // garbage bytes as Pokemon nicknames can throw (DSMysteryDungeonCharacterEncoding
                // isn't guaranteed total over arbitrary byte values).
                var bits = new BitBlock(candidate);
                if (bits.GetUInt(0, 0, 32) == Checksums.Calculate32BitChecksum(bits, 4, offsets.ChecksumEnd))
                {
                    return true;
                }
                return bits.GetUInt(offsets.BackupSaveStart, 0, 32) == Checksums.Calculate32BitChecksum(bits, offsets.BackupSaveStart + 4, offsets.BackupSaveStart + offsets.ChecksumEnd);
            });

            return payload != null ? new RBSave(payload) : null;
        }

        #region Checksums

        /// <summary>
        /// Checksum of the primary save
        /// </summary>
        public uint PrimaryChecksum { get; set; }

        /// <summary>
        /// Checksum of the secondary save
        /// </summary>
        public uint SecondaryChecksum { get; set; }

        /// <summary>
        /// Checksum of the QuickSave
        /// </summary>
        public uint QuicksaveChecksum { get; set; }

        /// <summary>
        /// Calculates the checksum of the primary save
        /// </summary>
        public virtual uint CalculatePrimaryChecksum()
        {
            return Checksums.Calculate32BitChecksum(Bits, 4, Offsets.ChecksumEnd);
        }

        /// <summary>
        /// Calculates the checksum of the backup save
        /// </summary>
        public virtual uint CalculateSecondaryChecksum()
        {
            return Checksums.Calculate32BitChecksum(Bits, Offsets.BackupSaveStart + 4, Offsets.BackupSaveStart + Offsets.ChecksumEnd);
        }        

        /// <summary>
        /// Determines whether or not the checksum of the primary save matches the primary save
        /// </summary>
        public bool IsPrimaryChecksumValid()
        {
            return PrimaryChecksum == CalculatePrimaryChecksum();
        }

        /// <summary>
        /// Determines whether or not the checksum of the backup save matches the backup save
        /// </summary>
        public bool IsSecondaryChecksumValid()
        {
            return SecondaryChecksum == CalculateSecondaryChecksum();
        }

        /// <summary>
        /// Updates all checksums to match current save data
        /// </summary>
        protected virtual void RecalculateChecksums()
        {
            PrimaryChecksum = CalculatePrimaryChecksum();
            SecondaryChecksum = CalculateSecondaryChecksum();
        }

        #endregion

        #region General
        /// <summary>
        /// The team name of the main game's exploration team
        /// </summary>
        public string TeamName { get; set; }

        /// <summary>
        /// The money held by the player in the main game
        /// </summary>
        public int HeldMoney { get; set; }

        /// <summary>
        /// The money stored in the bank
        /// </summary>
        public int StoredMoney { get; set; }

        /// <summary>
        /// The rank points held by the main game's rescue team
        /// </summary>
        public int RescueTeamPoints { get; set; }

        public int BaseType { get; set; }

        ///// <summary>
        ///// The rank held by the main game's exploration team
        ///// </summary>
        ///// <remarks>This proeprty wraps <see cref="ExplorerRankPoints"/>, so setting this property will reduce the number of explorer rank points held by the team.</remarks>
        //public TDExplorerRank ExplorerRank
        //{
        //    get
        //    {
        //        if (ExplorerRankPoints >= 62500)
        //            return TDExplorerRank.Master;
        //        else if (ExplorerRankPoints >= 15000)
        //            return TDExplorerRank.Hyper;
        //        else if (ExplorerRankPoints >= 10000)
        //            return TDExplorerRank.Ultra;
        //        else if (ExplorerRankPoints >= 6000)
        //            return TDExplorerRank.Super;
        //        else if (ExplorerRankPoints >= 3200)
        //            return TDExplorerRank.Diamond;
        //        else if (ExplorerRankPoints >= 1600)
        //            return TDExplorerRank.Gold;
        //        else if (ExplorerRankPoints >= 400)
        //            return TDExplorerRank.Silver;
        //        else if (ExplorerRankPoints >= 100)
        //            return TDExplorerRank.Bronze;
        //        else
        //            return TDExplorerRank.Normal;
        //    }
        //    set
        //    {
        //        ExplorerRankPoints = (int)value;
        //    }
        //}

        private void LoadGeneral(int baseOffset)
        {
            TeamName = Bits.GetStringPMD(0, baseOffset + Offsets.TeamNameStart, Offsets.TeamNameLength);
            HeldMoney = Bits.GetInt(0, baseOffset + Offsets.HeldMoneyOffset, Offsets.HeldMoneyLength);
            StoredMoney = Bits.GetInt(0, baseOffset + Offsets.StoredMoneyOffset, Offsets.StoredMoneyLength);
            RescueTeamPoints = Bits.GetInt(0, baseOffset + Offsets.RescuePointsOffset, Offsets.RescuePointsLength);
            BaseType = Bits.GetInt(0, baseOffset + Offsets.BaseTypeOffset, 8);
        }

        private void SaveGeneral()
        {
            Bits.SetStringPMD(0, Offsets.TeamNameStart, Offsets.TeamNameLength, TeamName);
            Bits.SetInt(0, Offsets.HeldMoneyOffset, Offsets.HeldMoneyLength, HeldMoney);
            Bits.SetInt(0, Offsets.StoredMoneyOffset, Offsets.StoredMoneyLength, StoredMoney);
            Bits.SetInt(0, Offsets.RescuePointsOffset, Offsets.RescuePointsLength, RescueTeamPoints);
            Bits.SetInt(0, Offsets.BaseTypeOffset, 8, BaseType);
        }

        #endregion

        #region Items

        /// <summary>
        /// The items stored in Kangaskhan's warehouse
        /// </summary>
        public List<RBStoredItem> StoredItems { get; set; }

        /// <summary>
        /// The items in the bag in the main game
        /// </summary>
        public List<RBHeldItem> HeldItems { get; set; }

        private void LoadItems(int baseOffset)
        {
            // Stored items
            StoredItems = new List<RBStoredItem>();
            var block = Bits.GetRange(baseOffset + Offsets.StoredItemOffset, Offsets.StoredItemCount * 10);
            for (int i = 0; i < Offsets.StoredItemCount; i++)
            {
                var quantity = block.GetNextInt(10);
                if (quantity > 0)
                {
                    StoredItems.Add(new RBStoredItem(i + 1, quantity));
                }
            }

            // Held Items (the bag): exactly INVENTORY_SIZE=20 slots in the decomp
            // (include/constants/item.h, teamItems[INVENTORY_SIZE] in str_items.h). This loop
            // previously ran to 50 (Explorers' bag size), reading 30 phantom slots past the
            // bag's end; the matching bug on the save side overwrote real storage data.
            HeldItems = new List<RBHeldItem>();
            for (int i = 0; i < Offsets.HeldItemCount; i++)
            {
                var item = new RBHeldItem(Bits.GetRange(baseOffset + Offsets.HeldItemOffset + (i * Offsets.HeldItemLength), Offsets.HeldItemLength));
                if (item.IsValid)
                {
                    HeldItems.Add(item);
                }
                else
                {
                    break;
                }
            }
        }

        private void SaveItems()
        {
            // Stored items
            var compiledItems = new Dictionary<int, int>(); // Key = item ID, value = quantity
            // - Combine the quantities
            foreach (var item in StoredItems)
            {
                if (!compiledItems.ContainsKey(item.ItemID))
                {
                    compiledItems.Add(item.ItemID, 0);
                }
                // 999 is the game's own storage clamp (src/items.c:979). The previous cap of
                // 1024 was doubly wrong: the quantity field is 10 bits, so writing 1024
                // truncated to 0 and silently deleted the whole stack, and 1000-1023 are values
                // the game itself can never produce.
                compiledItems[item.ItemID] = Math.Min(item.Quantity + compiledItems[item.ItemID], 999);
            }
            // - Update the save
            var block = new BitBlock(Offsets.StoredItemCount * 10);
            for (int i = 0;i<Offsets.StoredItemCount;i++)
            {
                if (compiledItems.ContainsKey(i+1))
                {
                    block.SetNextInt(10, compiledItems[i + 1]);
                }
                else
                {
                    block.SetNextInt(10, 0);
                }
            }
            Bits.SetRange(Offsets.StoredItemOffset, Offsets.StoredItemCount * 10, block);

            // Held items: write exactly the bag's 20 slots (INVENTORY_SIZE in the decomp).
            // This loop previously wrote 50 slots; the 30 extra 23-bit zero-fills ran 690 bits
            // past the bag's end, straight through the start of the storage-quantity array,
            // silently wiping the stored quantities of roughly the first 68 item IDs on every
            // save. Caught by a storage round-trip probe; see StoredItems_SurviveRoundTrip.
            for (int i = 0; i < Offsets.HeldItemCount; i++)
            {
                var index = Offsets.HeldItemOffset + i * Offsets.HeldItemLength;
                if (HeldItems.Count > i)
                {
                    Bits.SetRange(index, Offsets.HeldItemLength, HeldItems[i].GetHeldItemBits());
                }
                else
                {
                    Bits.SetRange(index, Offsets.HeldItemLength, new BitBlock(Offsets.HeldItemLength));
                }
            }
        }
        #endregion

        #region Stored Pokemon

        /// <summary>
        /// A snapshot of the save's state as loaded from the file (or as of the last save), used
        /// by <see cref="UpdateAdventureLogForRosterChanges"/> and the "Pending Changes" query
        /// methods (<see cref="IsSlotPending"/>, <see cref="IsFriendAreaPending"/>, etc.) to tell
        /// staged-but-unsaved edits apart from what's actually in the file. See
        /// <see cref="CaptureSnapshot"/>.
        /// </summary>
        private int _originalStoredPokemonCount;
        private HashSet<int> _originalRecruitedSpeciesIds = new HashSet<int>();

        private void LoadStoredPokemon(int baseOffset)
        {
            // The roster is a fixed 413-slot array, not a compact list -- occupied slots can be
            // scattered with gaps between them (confirmed against a real save file). Scan every
            // slot rather than stopping at the first empty one, or most of a real save's roster
            // silently disappears. A slot's occupancy is determined by Level (matching the
            // decomp's ReadPoke1Bits, which sets POKEMON_FLAG_EXISTS iff level != 0), not ID.
            StoredPokemon = new List<RBStoredPokemon>();
            for (int i = 0; i < Offsets.StoredPokemonCount; i++)
            {
                var pkm = new RBStoredPokemon(Bits.GetRange(baseOffset + Offsets.StoredPokemonOffset + i * Offsets.StoredPokemonLength, Offsets.StoredPokemonLength));

                if (pkm.Level <= 0)
                {
                    continue;
                }

                pkm.SlotIndex = i;
                StoredPokemon.Add(pkm);
            }
        }

        private void SaveStoredPokemon()
        {
            // Preserve every already-slotted Pokemon's original slot rather than compacting the
            // list into slots 0..N-1: the decomp writes separate "active team member" and "team
            // leader" indices right after this array (src/pokemon_3.c:SaveRecruitedPokemon) that
            // reference specific slot numbers. Those fields aren't modeled here, but their raw
            // bits are still present in the file -- relocating Pokemon on every save would
            // silently point them at the wrong roster entries. Newly-added Pokemon (SlotIndex
            // still -1, e.g. from MarkBossRecruited) get the lowest free slot instead.
            var occupied = new bool[Offsets.StoredPokemonCount];
            var unassigned = new List<RBStoredPokemon>();

            foreach (var pkm in StoredPokemon)
            {
                if (pkm.SlotIndex >= 0 && pkm.SlotIndex < Offsets.StoredPokemonCount)
                {
                    occupied[pkm.SlotIndex] = true;
                }
                else
                {
                    unassigned.Add(pkm);
                }
            }

            var nextFreeSlot = 0;
            foreach (var pkm in unassigned)
            {
                while (nextFreeSlot < Offsets.StoredPokemonCount && occupied[nextFreeSlot])
                {
                    nextFreeSlot++;
                }
                if (nextFreeSlot >= Offsets.StoredPokemonCount)
                {
                    throw new InvalidOperationException($"No free roster slot available for a newly-added Pokemon (roster is full at {Offsets.StoredPokemonCount} slots).");
                }
                pkm.SlotIndex = nextFreeSlot;
                occupied[nextFreeSlot] = true;
            }

            var bySlot = new RBStoredPokemon?[Offsets.StoredPokemonCount];
            foreach (var pkm in StoredPokemon)
            {
                bySlot[pkm.SlotIndex] = pkm;
            }

            for (int i = 0; i < Offsets.StoredPokemonCount; i++)
            {
                if (bySlot[i] != null)
                {
                    Bits.SetRange(Offsets.StoredPokemonOffset + i * Offsets.StoredPokemonLength, Offsets.StoredPokemonLength, bySlot[i]!.GetStoredPokemonBits());
                }
                else
                {
                    Bits.SetRange(Offsets.StoredPokemonOffset + i * Offsets.StoredPokemonLength, Offsets.StoredPokemonLength, new BitBlock(Offsets.StoredPokemonLength));
                }
            }
        }

        public List<RBStoredPokemon> StoredPokemon { get; set; }

        #endregion

        #region Friend Areas

        /// <summary>
        /// Which friend areas have been unlocked, indexed by <see cref="RBFriendArea"/>.
        /// Index 0 (<see cref="RBFriendArea.None"/>) is a real bit in the save but is never
        /// set by the game.
        /// </summary>
        public bool[] FriendAreasUnlocked { get; set; } = new bool[0];

        /// <summary>
        /// Unlocks a friend area, e.g. so a recruited Pokemon whose species belongs there can be
        /// visited without the area itself still showing as locked.
        /// </summary>
        /// <returns>True if the area was newly unlocked; false if it was already unlocked.</returns>
        public bool UnlockFriendArea(RBFriendArea area)
        {
            var index = (int)area;
            var alreadyUnlocked = FriendAreasUnlocked[index];
            FriendAreasUnlocked[index] = true;
            return !alreadyUnlocked;
        }

        /// <summary>
        /// Adds a Pokemon to the recruited roster matching a <see cref="RecruitGuideEntry"/> --
        /// same species, level, and stats a legitimately recruited individual would have at its
        /// easiest real recruit spot (see <see cref="RBRecruitGuide"/>), "met at" set to that
        /// spot's dungeon and floor, placed in a free roster slot within
        /// <paramref name="entry"/>'s friend area (see <see cref="RBFriendAreaCapacity"/> for why
        /// that matters -- which area a Pokemon "lives in" is determined by its roster slot, not
        /// its species), and the area auto-unlocked if it wasn't already. If the species is also
        /// a story boss with a cutscene "complete" flag (e.g. the legendary birds, recruitable
        /// both from here and from <see cref="MarkBossRecruited"/>), that flag is set too -- see
        /// <see cref="RBBossEncounters"/> for why both pieces of state need to move together
        /// regardless of which method added the Pokemon. The Adventure Log's recruited-count/
        /// species-flag are NOT updated here -- <see cref="PreSave"/> computes that once, as a net
        /// diff against the roster as it was when the file was loaded (see
        /// <see cref="UpdateAdventureLogForRosterChanges"/>), so adding a Pokemon and then
        /// removing it again (e.g. via <c>StoredPokemon.Remove</c>) before saving doesn't count
        /// it at all.
        /// </summary>
        /// <returns>The newly-added Pokemon.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="entry"/>'s friend area is already full.</exception>
        public RBStoredPokemon RecruitFromGuide(RecruitGuideEntry entry)
        {
            var slot = FindFreeSlotInFriendArea(entry.FriendArea);
            if (slot < 0)
            {
                throw new InvalidOperationException($"{entry.FriendArea} is full ({RBFriendAreaCapacity.Capacity(entry.FriendArea)} slots) -- can't add another {entry.SpeciesName} there.");
            }

            var pokemon = entry.ToStoredPokemon();
            pokemon.SlotIndex = slot;
            StoredPokemon.Add(pokemon);

            UnlockFriendArea(entry.FriendArea);
            ExclusivePokemonData.MarkBossDefeated(entry.SpeciesId);

            return pokemon;
        }

        /// <summary>
        /// Finds a free roster slot within <paramref name="area"/>'s dedicated slot range (see
        /// <see cref="RBFriendAreaCapacity"/>), or -1 if the area is already full.
        /// </summary>
        public int FindFreeSlotInFriendArea(RBFriendArea area)
        {
            var (start, count) = RBFriendAreaCapacity.SlotRange(area);
            var occupied = new HashSet<int>(StoredPokemon.Where(p => p.SlotIndex >= 0).Select(p => p.SlotIndex));
            for (var slot = start; slot < start + count; slot++)
            {
                if (!occupied.Contains(slot))
                {
                    return slot;
                }
            }
            return -1;
        }

        private void LoadFriendAreas(int baseOffset)
        {
            var bits = Bits.GetRange(baseOffset + Offsets.FriendAreaOffset, Offsets.FriendAreaCount);
            FriendAreasUnlocked = new bool[Offsets.FriendAreaCount];
            for (int i = 0; i < Offsets.FriendAreaCount; i++)
            {
                FriendAreasUnlocked[i] = bits[i];
            }
        }

        private void SaveFriendAreas()
        {
            var bits = new BitBlock(Offsets.FriendAreaCount);
            for (int i = 0; i < Offsets.FriendAreaCount; i++)
            {
                bits[i] = FriendAreasUnlocked[i];
            }
            Bits.SetRange(Offsets.FriendAreaOffset, Offsets.FriendAreaCount, bits);
        }

        #endregion

        #region Adventure Log

        /// <summary>
        /// The Adventure Log's "Pokemon recruited" counter (<c>numJoined</c> in the decomp's
        /// AdventureLog struct, src/adventure_info.c). Unlike <see cref="StoredPokemon"/>.Count,
        /// this is a monotonic counter the game increments once per recruit event
        /// (<c>IncrementAdventureNumJoined</c>, called from src/dungeon_mon_recruit.c,
        /// src/ground_script.c, and src/pokemon_evolution.c) -- it is never recomputed from the
        /// roster's actual contents. See <see cref="UpdateAdventureLogForRosterChanges"/> for how
        /// this library keeps it in sync when adding/removing roster entries directly.
        /// </summary>
        public int NumPokemonRecruited
        {
            get => Bits.GetInt(0, Offsets.NumPokemonRecruitedOffset, Offsets.NumPokemonRecruitedBitLength);
            set => Bits.SetInt(0, Offsets.NumPokemonRecruitedOffset, Offsets.NumPokemonRecruitedBitLength, value);
        }

        /// <summary>
        /// Whether a species has ever been recruited (<c>unk1C</c> in the decomp's AdventureLog
        /// struct) -- a persistent per-species flag distinct from the roster itself, used for
        /// achievements (e.g. "recruited Moltres") and other Adventure Log displays. Doesn't
        /// fold alternate-form species (Castform/Deoxys formes, the Rayquaza cutscene id) down
        /// to their base species the way the decomp's <c>GetBaseSpeciesNoUnown</c> does -- not
        /// needed in practice since nothing <see cref="RBRecruitGuide"/> emits uses those ids.
        /// </summary>
        public bool HasRecruitedSpeciesFlag(int speciesId)
        {
            var (wordOffset, mask) = RecruitedSpeciesFlagBit(speciesId);
            return (Bits.GetInt(0, wordOffset, 32) & mask) != 0;
        }

        /// <summary>Sets a species' recruited flag. See <see cref="HasRecruitedSpeciesFlag"/>.</summary>
        public void SetRecruitedSpeciesFlag(int speciesId)
        {
            var (wordOffset, mask) = RecruitedSpeciesFlagBit(speciesId);
            var word = Bits.GetInt(0, wordOffset, 32);
            Bits.SetInt(0, wordOffset, 32, word | mask);
        }

        private (int WordOffset, int Mask) RecruitedSpeciesFlagBit(int speciesId)
        {
            if (speciesId < 0 || speciesId >= Offsets.RecruitedSpeciesCount)
            {
                throw new ArgumentOutOfRangeException(nameof(speciesId));
            }
            var word = speciesId / 32;
            var bit = speciesId % 32;
            return (Offsets.RecruitedSpeciesFlagsOffset + word * 32, 1 << bit);
        }

        /// <summary>
        /// Updates <see cref="NumPokemonRecruited"/> and each newly-appearing species'
        /// recruited-flag (<see cref="SetRecruitedSpeciesFlag"/>) once, as a single net diff
        /// between <see cref="StoredPokemon"/> as it is right now and as it was when the file
        /// was loaded (or since the last save -- see <see cref="CaptureSnapshot"/>, called at the
        /// end of <see cref="PreSave"/>). Called from <see cref="PreSave"/>, not from
        /// <see cref="RecruitFromGuide"/> or <see cref="MarkBossRecruited"/> directly, so that
        /// e.g. adding two Pokemon and then removing one again before saving only counts as +1,
        /// not +2 (and the removed one's species doesn't get flagged at all if it wasn't already
        /// recruited).
        /// </summary>
        private void UpdateAdventureLogForRosterChanges()
        {
            var netAdded = StoredPokemon.Count - _originalStoredPokemonCount;
            if (netAdded > 0)
            {
                // The decomp only increments numJoined while it's below 9999 -- 9999 is the
                // highest value the game's own logic can ever produce, so clamp to match.
                NumPokemonRecruited = Math.Min(9999, NumPokemonRecruited + netAdded);
            }

            foreach (var pkm in StoredPokemon)
            {
                if (!_originalRecruitedSpeciesIds.Contains(pkm.ID))
                {
                    SetRecruitedSpeciesFlag(pkm.ID);
                }

                // Both of the game's own roster-add paths (CreateAndAddPokemon and the
                // friend-area add in src/pokemon.c) call SetMonSeenFlag on the joining species,
                // and nothing ever recomputes seen flags later, so "in roster" must imply
                // "seen". Enforce that invariant for every roster entry (idempotent for
                // organically-recruited ones). Without this, a tool-added recruit's species
                // stays invisible to everything gated on GetMonSeenFlag: Wigglytuff's friend
                // area shop listing, mail/job client generation (pokemon_mail.c), and story NPC
                // checks like the Ho-Oh one in ground_script.c.
                if (pkm.ID >= 0 && pkm.ID < RBExclusivePokemonData.MonsterSeenFlagCount)
                {
                    ExclusivePokemonData.MonsterSeenFlags[pkm.ID] = true;
                }

                // Same shape of invariant for friend areas: organically, recruiting requires
                // already owning the camp, and the game decides area membership purely by the
                // slot-range partition (sub_80923D4) -- so "slot occupied" must imply "that
                // slot's area is unlocked". Idempotent for organic saves; corrects direct API
                // adds that bypassed RecruitFromGuide/UnlockFriendArea. Runs after
                // SaveStoredPokemon so newly-added Pokemon already have a real SlotIndex.
                var area = RBFriendAreaCapacity.AreaForSlot(pkm.SlotIndex);
                if (area != RBFriendArea.None && (int)area < FriendAreasUnlocked.Length)
                {
                    FriendAreasUnlocked[(int)area] = true;
                }
            }
        }

        #endregion

        #region Pending Changes

        private bool[] _originalFriendAreasUnlocked = Array.Empty<bool>();
        private int _originalHeldMoney;
        private int _originalStoredMoney;
        private int _originalRescueTeamPoints;
        private Dictionary<int, int> _originalStoredItemQuantities = new Dictionary<int, int>();
        private HashSet<int> _originalOccupiedSlots = new HashSet<int>();

        /// <summary>
        /// Snapshots the save's current state so the methods below can tell staged-but-unsaved
        /// edits apart from what's actually in the file. Called once after loading and again at
        /// the end of every <see cref="PreSave"/>, so after a save the snapshot represents the
        /// file just written, not the one originally opened.
        /// </summary>
        private void CaptureSnapshot()
        {
            _originalStoredPokemonCount = StoredPokemon.Count;
            _originalRecruitedSpeciesIds = new HashSet<int>(StoredPokemon.Select(p => p.ID));
            _originalOccupiedSlots = new HashSet<int>(StoredPokemon.Select(p => p.SlotIndex));
            _originalFriendAreasUnlocked = (bool[])FriendAreasUnlocked.Clone();
            _originalHeldMoney = HeldMoney;
            _originalStoredMoney = StoredMoney;
            _originalRescueTeamPoints = RescueTeamPoints;
            _originalStoredItemQuantities = StoredItems
                .GroupBy(i => i.ItemID)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        }

        /// <summary>
        /// True if <paramref name="slotIndex"/> holds a Pokemon that wasn't in the roster when
        /// the file was loaded (or since the last save) -- i.e. a staged-but-unsaved recruit.
        /// </summary>
        public bool IsSlotPending(int slotIndex) => !_originalOccupiedSlots.Contains(slotIndex);

        /// <summary>
        /// True if <paramref name="area"/> is unlocked right now but wasn't when the file was
        /// loaded (or since the last save).
        /// </summary>
        public bool IsFriendAreaPending(RBFriendArea area)
        {
            var index = (int)area;
            return FriendAreasUnlocked[index] && !_originalFriendAreasUnlocked[index];
        }

        /// <summary>Unsaved change to <see cref="HeldMoney"/> since the file was loaded (or last saved).</summary>
        public int HeldMoneyDelta => HeldMoney - _originalHeldMoney;

        /// <summary>Unsaved change to <see cref="StoredMoney"/> since the file was loaded (or last saved).</summary>
        public int StoredMoneyDelta => StoredMoney - _originalStoredMoney;

        /// <summary>Unsaved change to <see cref="RescueTeamPoints"/> since the file was loaded (or last saved).</summary>
        public int RescueTeamPointsDelta => RescueTeamPoints - _originalRescueTeamPoints;

        /// <summary>
        /// Unsaved change to how many of <paramref name="itemId"/> are in
        /// <see cref="StoredItems"/>, since the file was loaded (or last saved).
        /// </summary>
        public int PendingItemDelta(int itemId)
        {
            var current = StoredItems.Where(i => i.ItemID == itemId).Sum(i => i.Quantity);
            var original = _originalStoredItemQuantities.TryGetValue(itemId, out var q) ? q : 0;
            return current - original;
        }

        #endregion

        #region Boss Encounters

        /// <summary>
        /// Story-progress data (cutscene flags, Pokedex "seen" flags, etc.).
        /// </summary>
        /// <remarks>
        /// Loaded from and saved to <see cref="RBOffsets.ExclusivePokemonDataOffset"/>,
        /// part of the same contiguous bitstream as <see cref="RBOffsets.AdventureDataOffset"/>
        /// (GameOptions + PlayTime + AdventureBits + this). See that offset's remarks for how
        /// it was determined.
        /// </remarks>
        public RBExclusivePokemonData ExclusivePokemonData { get; set; } = new RBExclusivePokemonData();

        /// <summary>
        /// The mail block: mailbox, Pelipper board jobs, accepted jobs, and the
        /// used-Wonder-Mail history. See <see cref="RBMailData"/> and
        /// <see cref="RBOffsets.MailDataOffset"/>.
        /// </summary>
        public RBMailData MailData { get; set; } = new RBMailData();

        /// <summary>
        /// Marks a story boss (e.g. <see cref="RBBossEncounters.Zapdos"/>) as
        /// already fought and successfully recruited: adds it to
        /// <see cref="StoredPokemon"/> if not already present, and sets its
        /// cutscene "complete" flag in <see cref="ExclusivePokemonData"/> if
        /// that boss has one. Setting both together avoids a save where the
        /// game still expects the boss to be fought (see
        /// <see cref="RBBossEncounters"/> for why both pieces of state exist).
        /// If the species has a known friend area (<see cref="RBRecruitGuide.HomeAreaOf"/> --
        /// true for the bird trio, not for one-off story bosses without a habitat), the new
        /// entry is placed in a free slot within that area and the area is auto-unlocked, same
        /// as <see cref="RecruitFromGuide"/>. As with that method, the Adventure Log is not
        /// updated here -- see <see cref="UpdateAdventureLogForRosterChanges"/>.
        /// </summary>
        /// <param name="bossMonsterId">RB monster ID of the boss, e.g. <see cref="RBBossEncounters.Lugia"/>.</param>
        /// <param name="pokemon">
        /// The Pokemon to add to the roster if not already recruited. Its
        /// <see cref="RBStoredPokemon.ID"/> must match <paramref name="bossMonsterId"/>.
        /// </param>
        /// <returns>True if a new roster entry was added; false if the boss was already in <see cref="StoredPokemon"/>.</returns>
        public bool MarkBossRecruited(int bossMonsterId, RBStoredPokemon pokemon)
        {
            if (pokemon == null)
            {
                throw new ArgumentNullException(nameof(pokemon));
            }
            if (pokemon.ID != bossMonsterId)
            {
                throw new ArgumentException($"{nameof(pokemon)}.{nameof(RBStoredPokemon.ID)} ({pokemon.ID}) does not match {nameof(bossMonsterId)} ({bossMonsterId}).", nameof(pokemon));
            }

            var alreadyRecruited = StoredPokemon.Exists(p => p.ID == bossMonsterId);
            if (!alreadyRecruited)
            {
                var homeArea = RBRecruitGuide.HomeAreaOf(bossMonsterId);
                if (homeArea.HasValue)
                {
                    var slot = FindFreeSlotInFriendArea(homeArea.Value);
                    if (slot < 0)
                    {
                        throw new InvalidOperationException($"{homeArea.Value} is full ({RBFriendAreaCapacity.Capacity(homeArea.Value)} slots) -- can't add another {pokemon.Name} there.");
                    }
                    pokemon.SlotIndex = slot;
                    UnlockFriendArea(homeArea.Value);
                }

                StoredPokemon.Add(pokemon);
            }

            ExclusivePokemonData.MarkBossDefeated(bossMonsterId);

            return !alreadyRecruited;
        }

        /// <summary>
        /// Whether <paramref name="speciesId"/> could actually be recruited right now if the
        /// recruit roll succeeds -- i.e. whether the real game's <c>IsMonsterRecruitable</c> would
        /// pass, ignoring the roll itself. True for anything not gated at all.
        /// </summary>
        /// <remarks>
        /// Three cases, deliberately checked differently:
        /// <list type="bullet">
        /// <item>A species in <see cref="RBBossEncounters.NeverCombatRecruitable"/> (Latios/Latias)
        /// is always false -- not "not yet," but never, at any point in the game, per the decomp.</item>
        /// <item>For a species in <see cref="RBBossEncounters.FirstEncounterFlagsByBoss"/>, checks
        /// whether its listed <see cref="RBCutsceneFlag"/> is set in
        /// <see cref="ExclusivePokemonData"/> -- a real persisted flag, only ever set by that boss's
        /// own first-encounter faint handler.</item>
        /// <item>For a Regi (<see cref="RBBossEncounters.RegiItems"/>), checks whether its Part (or
        /// the Music Box) is currently held or in storage -- per dungeon_cutscene_regis.c in the
        /// decomp, this is what the game itself checks each time a Regi's room is entered,
        /// recomputing <see cref="RBCutsceneFlag.RegiItemObtained"/> fresh from current inventory
        /// rather than reading a persisted flag, so this method deliberately doesn't read that flag
        /// at all.</item>
        /// </list>
        /// Everything else returns true (Lugia, Kyogre, Deoxys, Celebi, Jirachi -- none of them
        /// gated by either <c>MonCutsceneCompleted</c> or a story-blocked <c>IsRecruitingEnabled</c>
        /// dungeon; confirmed against the real <c>gDungeons[]</c> data and against community
        /// recruitment guides, not just traced code -- see RECRUIT_MECHANICS.md).
        /// </remarks>
        public bool CanCurrentlyRecruit(int speciesId)
        {
            if (RBBossEncounters.NeverCombatRecruitable.Contains(speciesId))
            {
                return false;
            }
            if (RBBossEncounters.RegiItems.PartIdsBySpecies.TryGetValue(speciesId, out var partItemId))
            {
                return HasItem(partItemId) || HasItem(RBBossEncounters.RegiItems.MusicBoxItemId);
            }
            return ExclusivePokemonData.HasCompletedFirstEncounter(speciesId);
        }

        private bool HasItem(int itemId) =>
            StoredItems.Any(i => i.ItemID == itemId && i.Quantity > 0) ||
            HeldItems.Any(i => i.IsValid && i.ID == itemId);

        #endregion

        #region Functions

        public override async Task OpenFile(string filename, IFileSystem provider)
        {
            await base.OpenFile(filename, provider);
            Init();
        }

        private void Init()
        {
            // Checksums
            PrimaryChecksum = Bits.GetUInt(0, 0, 32);
            SecondaryChecksum = Bits.GetUInt(Offsets.BackupSaveStart, 0, 32);

            // Use the backup save if the first one's checksum is not valid
            // If both are invalid, use the first one
            //
            // baseOffset is in BITS (BackupSaveStart itself is a byte offset), because every
            // loader below adds it to bit offsets. A previous version passed the byte value
            // straight through, and some loaders ignored it entirely -- so a save that actually
            // needed its backup block was read as garbage from 0xC00 bytes in, and PreSave would
            // then have written that garbage back out with freshly valid checksums. See the
            // fallback test in RBSaveDataTests for the regression guard.
            var baseOffset = 0;
            if (!IsPrimaryChecksumValid() && IsSecondaryChecksumValid())
            {
                baseOffset = Offsets.BackupSaveStart * 8;
            }

            LoadGeneral(baseOffset);
            LoadItems(baseOffset);
            LoadStoredPokemon(baseOffset);
            LoadFriendAreas(baseOffset);
            LoadExclusivePokemonData(baseOffset);
            LoadMailData(baseOffset);

            CaptureSnapshot();
        }

        private void LoadExclusivePokemonData(int baseOffset)
        {
            ExclusivePokemonData = new RBExclusivePokemonData(Bits.GetRange(baseOffset + Offsets.ExclusivePokemonDataOffset, RBExclusivePokemonData.BitLength));
        }

        private void SaveExclusivePokemonData()
        {
            Bits.SetRange(Offsets.ExclusivePokemonDataOffset, RBExclusivePokemonData.BitLength, ExclusivePokemonData.ToBitBlock());
        }

        private void LoadMailData(int baseOffset)
        {
            MailData = new RBMailData(Bits.GetRange(baseOffset + Offsets.MailDataOffset, RBMailData.BitLength));
        }

        private void SaveMailData()
        {
            Bits.SetRange(Offsets.MailDataOffset, RBMailData.BitLength, MailData.ToBitBlock());
        }

        /// <summary>
        /// Clamps every writable field to the limits the game itself enforces, so a tool-edited
        /// save can't contain values the game could never produce. Sources (pret/pmd-red):
        /// MAX_TEAM_MONEY 99999 and MAX_TEAM_SAVINGS 9999999 (include/constants/item.h), IQ
        /// clamped to 999 (src/items.c:879), storage quantity clamped to 999 (src/items.c:979),
        /// level cap 100, max HP cap 999. Level is additionally floored at 1 because level 0 is
        /// how the roster marks an empty slot -- writing 0 would silently delete the Pokemon.
        /// Out-of-range values would otherwise also overflow their bitfields (money is 24 bits,
        /// IQ/HP 10 bits, level 7 bits) and wrap into garbage.
        /// </summary>
        private void ClampToGameLimits()
        {
            HeldMoney = Math.Clamp(HeldMoney, 0, 99999);
            StoredMoney = Math.Clamp(StoredMoney, 0, 9999999);
            RescueTeamPoints = Math.Max(0, RescueTeamPoints);

            foreach (var pkm in StoredPokemon)
            {
                pkm.Level = Math.Clamp(pkm.Level, 1, 100);
                pkm.IQ = Math.Clamp(pkm.IQ, 0, 999);
                pkm.HP = Math.Clamp(pkm.HP, 1, 999);

                // Held item: id 0-239 (0 = nothing; 239 is the last real item id), quantity is
                // a 7-bit field the game only uses for thrown stacks. Do NOT zero the quantity
                // when the id is 0: organic saves routinely carry stale nonzero quantity bits on
                // itemless Pokemon (the game serializes the RAM struct's leftover bytes and only
                // ever reads the field behind an id check). Normalizing them would change bytes
                // the game itself preserves -- a tool fingerprint. Verified on a real save:
                // resaving with no edits is byte-identical only if these bits pass through.
                pkm.HeldItemId = Math.Clamp(pkm.HeldItemId, 0, 239);
                pkm.HeldItemQuantity = Math.Clamp(pkm.HeldItemQuantity, 0, 127);
            }

            foreach (var item in HeldItems)
            {
                item.ID = Math.Clamp(item.ID, 0, 239);
                item.Parameter = Math.Clamp(item.Parameter, 0, 127);
            }
        }

        protected override void PreSave()
        {
            ClampToGameLimits();
            SaveGeneral();
            SaveItems();
            // SaveStoredPokemon must run before UpdateAdventureLogForRosterChanges: it assigns
            // real slot indices to newly-added Pokemon, which the roster-implies-area-unlocked
            // invariant needs. SaveFriendAreas and SaveExclusivePokemonData must run after,
            // since the update mutates FriendAreasUnlocked and ExclusivePokemonData.
            SaveStoredPokemon();
            UpdateAdventureLogForRosterChanges();
            SaveFriendAreas();
            SaveExclusivePokemonData();
            SaveMailData();

            // Copy primary save to backup save. BackupSaveStart is a *byte* offset (used as
            // such everywhere else, e.g. GetUInt(Offsets.BackupSaveStart, 0, 32) for the backup
            // checksum) but bit-range methods operate in *bits* -- multiply by 8 to convert.
            // A previous version of this line passed BackupSaveStart directly as a bit offset,
            // which silently copied a much-too-short, misaligned slice of the primary save back
            // onto itself (corrupting roughly bytes [3072, 24576) on every save) instead of
            // copying the primary save into the backup region. Caught by round-tripping a real
            // save file with roster entries spread across that byte range.
            Bits.CopyRangeWithin(32, Offsets.BackupSaveStart * 8 + 32, Offsets.BackupSaveStart * 8 - 32);

            // Checksums
            RecalculateChecksums();
            Bits.SetUInt(0, 0, 32, PrimaryChecksum);
            Bits.SetUInt(Offsets.BackupSaveStart, 0, 32, SecondaryChecksum);

            // Everything just written is now "the file" -- refresh the pending-changes snapshot
            // so IsSlotPending/IsFriendAreaPending/etc. stop reporting it as staged.
            CaptureSnapshot();
        }

        /// <summary>
        /// Determines whether or not the given file is a save file for Pokémon Mystery Dungeon: Explorers of Time and Darkness.
        /// </summary>
        /// <param name="file">The file to be checked</param>
        /// <returns>A boolean indicating whether or not the given file is supported by this class</returns>
        public virtual async Task<bool> IsOfType(GenericFile file)
        {
            if (file.Length > Offsets.ChecksumEnd)
            {
                return await file.ReadUInt32Async(0) == Checksums.Calculate32BitChecksum(file, 4, Offsets.ChecksumEnd);
            }
            else
            {
                return false;
            }
        }

        public override byte[] ToByteArray()
        {
            // BitBlockFile.ToByteArray() already calls PreSave() (virtual dispatch resolves it
            // to this class's override) -- an extra explicit call here just ran the whole save
            // pipeline twice on every ToByteArray() call for no reason.
            return base.ToByteArray();
        }

        #endregion
    }
}
