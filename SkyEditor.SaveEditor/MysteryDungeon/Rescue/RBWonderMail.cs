using System;
using System.Linq;
using System.Text;

namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    /// <summary>
    /// One Wonder Mail / job / mailbox entry in a Red/Blue Rescue Team save.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>WonderMail</c> and its 93-bit serialization <c>ReadWonderMailBits</c>/
    /// <c>WriteWonderMailBits</c> (src/code_80958E8.c) in the pret/pmd-red decomp. The same
    /// 93-bit payload is what a 24-character Wonder Mail password encodes (see
    /// <see cref="RBWonderMailPassword"/>), which is why passwords can be regenerated from
    /// stored jobs.
    /// </remarks>
    public class RBWonderMail
    {
        public const int BitLength = 93;

        /// <summary>WONDER_MAIL_TYPE_NONE in the decomp: the empty-slot marker.</summary>
        public const int MailTypeNone = 0;

        /// <summary>WONDER_MAIL_TYPE_WONDER in the decomp: a Wonder Mail job (password-representable).</summary>
        public const int MailTypeWonder = 5;

        /// <summary>The dungeon id the game's slot-reset routines write (no real dungeon).</summary>
        public const int EmptyDungeonId = 99;

        public int MailType { get; set; }
        public int MissionType { get; set; }

        /// <summary>Flavor-text message variant (the decomp's unk2; valid range 0-9 per ValidateWonderMail).</summary>
        public int FlavorTextType { get; set; }

        public int ClientSpecies { get; set; }
        public int TargetSpecies { get; set; }
        public int TargetItem { get; set; }
        public int RewardType { get; set; }
        public int RewardItem { get; set; }
        public int RewardFriendArea { get; set; }

        /// <summary>24-bit mission RNG seed (DungeonMailSeed.seed).</summary>
        public int Seed { get; set; }

        /// <summary>Dungeon id, indexed like <see cref="Lists.RBLocations"/>.</summary>
        public int DungeonId { get; set; }

        public int Floor { get; set; }

        /// <summary>The game checks only mailType to decide slot emptiness (IsJobSlotEmpty/IsMailSlotEmpty).</summary>
        public bool IsEmpty => MailType == MailTypeNone;

        public bool IsWonderMail => MailType == MailTypeWonder;

        public RBWonderMail()
        {
        }

        /// <param name="bits">Exactly <see cref="BitLength"/> bits, as stored in the save.</param>
        public RBWonderMail(BitBlock bits)
        {
            bits.Position = 0;
            MailType = bits.GetNextInt(4);
            MissionType = bits.GetNextInt(3);
            FlavorTextType = bits.GetNextInt(4);
            ClientSpecies = bits.GetNextInt(9);
            TargetSpecies = bits.GetNextInt(9);
            TargetItem = bits.GetNextInt(8);
            RewardType = bits.GetNextInt(4);
            RewardItem = bits.GetNextInt(8);
            RewardFriendArea = bits.GetNextInt(6);
            Seed = bits.GetNextInt(24);
            DungeonId = bits.GetNextInt(7);
            Floor = bits.GetNextInt(7);
        }

        public BitBlock ToBitBlock()
        {
            var bits = new BitBlock(BitLength) { Position = 0 };
            bits.SetNextInt(4, MailType);
            bits.SetNextInt(3, MissionType);
            bits.SetNextInt(4, FlavorTextType);
            bits.SetNextInt(9, ClientSpecies);
            bits.SetNextInt(9, TargetSpecies);
            bits.SetNextInt(8, TargetItem);
            bits.SetNextInt(4, RewardType);
            bits.SetNextInt(8, RewardItem);
            bits.SetNextInt(6, RewardFriendArea);
            bits.SetNextInt(24, Seed);
            bits.SetNextInt(7, DungeonId);
            bits.SetNextInt(7, Floor);
            return bits;
        }

        /// <summary>Human-readable mission line, e.g. "Help me: Manectric (Frosty Forest 6F)".</summary>
        public string GetMissionSummary()
        {
            var client = SpeciesName(ClientSpecies);
            var place = $"{DungeonName(DungeonId)} {Floor}F";
            return MissionType switch
            {
                0 => $"Help me: {client} ({place})",
                1 => $"Find {SpeciesName(TargetSpecies)} for {client} ({place})",
                2 => $"Escort {client} to {SpeciesName(TargetSpecies)} ({place})",
                3 => $"Find {ItemName(TargetItem)} for {client} ({place})",
                4 => $"Deliver {ItemName(TargetItem)} to {client} ({place})",
                _ => $"Mission type {MissionType}: {client} ({place})",
            };
        }

        /// <summary>Human-readable reward line, per the decomp's RewardType enum.</summary>
        public string GetRewardSummary() => RewardType switch
        {
            0 or 5 => "Money",
            1 or 6 => "Money + bonus",
            2 or 7 => $"Item: {ItemName(RewardItem)}",
            3 or 8 => $"Item: {ItemName(RewardItem)} + bonus",
            9 => $"Friend Area: {(RBFriendArea)RewardFriendArea}",
            _ => $"Reward type {RewardType}",
        };

        private static string SpeciesName(int id) => Lists.RBPokemon.TryGetValue(id, out var name) ? name : $"#{id}";

        private static string ItemName(int id) => Lists.RBItems.TryGetValue(id, out var name) ? name : $"item #{id}";

        internal static string DungeonName(int id) => Lists.RBLocations.TryGetValue(id, out var name) ? name : $"dungeon #{id}";

        /// <summary>
        /// An empty slot exactly as the game's own reset routines write one:
        /// mailType none, dungeon 99, floor 0, and the given rewardType
        /// (MONEY1=5 for mailbox/job slots per ResetMailboxSlot/ResetJobSlot,
        /// 0 for Pelipper board slots per ResetPelipperBoardSlot). All other fields zero.
        /// </summary>
        public static RBWonderMail CreateEmptySlot(int emptyRewardType) => new RBWonderMail
        {
            MailType = MailTypeNone,
            DungeonId = EmptyDungeonId,
            Floor = 0,
            RewardType = emptyRewardType,
        };
    }

    /// <summary>
    /// The 24-character Wonder Mail password codec for Red/Blue Rescue Team.
    /// </summary>
    /// <remarks>
    /// A password is the mail's 93-bit payload packed LSB-first into 14 bytes, prefixed with a
    /// 1-byte checksum (sum of payload[i] + i + 1, mod 256), split into 24 five-bit codes,
    /// shuffled by a fixed permutation, and mapped through a 32-character alphabet. Ported from
    /// the wondermail JS library (samlowedotdev/assets/js/wondermail, a clean-room implementation
    /// of publicly documented behavior); field order matches the save's own WriteWonderMailBits,
    /// so <see cref="RBWonderMail"/> round-trips through it directly.
    /// </remarks>
    public static class RBWonderMailPassword
    {
        public const int PasswordLength = 24;

        private const string Chars = "?67NPR89F0+.STXY45MCHJ-K12!%3Q#W";

        // shuffled[Shuffle[j]] = codes[j]; inverting on decode.
        private static readonly int[] Shuffle = { 12, 6, 19, 8, 4, 13, 15, 9, 16, 2, 20, 18, 0, 21, 11, 5, 23, 3, 17, 10, 1, 14, 22, 7 };

        public static string Encode(RBWonderMail mail)
        {
            var payload = new byte[14];
            var bitPos = 0;
            void Write(int value, int width)
            {
                for (int k = 0; k < width; k++, bitPos++)
                {
                    if (((value >> k) & 1) != 0)
                    {
                        payload[bitPos / 8] |= (byte)(1 << (bitPos % 8));
                    }
                }
            }

            Write(mail.MailType, 4);
            Write(mail.MissionType, 3);
            Write(mail.FlavorTextType, 4);
            Write(mail.ClientSpecies, 9);
            Write(mail.TargetSpecies, 9);
            Write(mail.TargetItem, 8);
            Write(mail.RewardType, 4);
            Write(mail.RewardItem, 8);
            Write(mail.RewardFriendArea, 6);
            Write(mail.Seed, 24);
            Write(mail.DungeonId, 7);
            Write(mail.Floor, 7);

            var full = new byte[15];
            full[0] = ChecksumOf(payload);
            payload.CopyTo(full, 1);

            var password = new char[PasswordLength];
            for (int i = 0; i < PasswordLength; i++)
            {
                var code = 0;
                for (int k = 0; k < 5; k++)
                {
                    var j = i * 5 + k;
                    code |= ((full[j / 8] >> (j % 8)) & 1) << k;
                }
                password[Shuffle[i]] = Chars[code];
            }
            return new string(password);
        }

        /// <summary>
        /// Decodes a password back into its mail payload, or returns null if it's malformed
        /// (wrong length after normalization, bad characters, or bad checksum). Accepts the
        /// conventional '#'/'%'/'.' stand-ins as well as the real ♂/♀/… glyphs, and ignores
        /// whitespace and quotes ('-' is a real password character, so it's kept).
        /// </summary>
        public static RBWonderMail? Decode(string? rawPassword)
        {
            var password = Normalize(rawPassword);
            if (password.Length != PasswordLength)
            {
                return null;
            }

            var full = new byte[15];
            for (int i = 0; i < PasswordLength; i++)
            {
                var code = Chars.IndexOf(password[Shuffle[i]]);
                if (code < 0)
                {
                    return null;
                }
                for (int k = 0; k < 5; k++)
                {
                    var j = i * 5 + k;
                    if (((code >> k) & 1) != 0)
                    {
                        full[j / 8] |= (byte)(1 << (j % 8));
                    }
                }
            }

            var payload = full.Skip(1).ToArray();
            if (ChecksumOf(payload) != full[0])
            {
                return null;
            }

            var bits = new BitBlock(payload);
            return new RBWonderMail(bits.GetRange(0, RBWonderMail.BitLength));
        }

        /// <summary>Grouped 4-4-4 / 4-4-4 with the real ♂/♀/… glyphs, the way passwords are shown in-game.</summary>
        public static string FormatForDisplay(string password)
        {
            var pretty = password.Replace('#', '♂').Replace('%', '♀').Replace('.', '…');
            var groups = Enumerable.Range(0, (pretty.Length + 3) / 4).Select(i => pretty.Substring(i * 4, Math.Min(4, pretty.Length - i * 4)));
            return string.Join(" ", groups);
        }

        private static byte ChecksumOf(byte[] payload)
        {
            var sum = 0;
            for (int i = 0; i < payload.Length; i++)
            {
                sum = (sum + payload[i] + i + 1) & 0xFF;
            }
            return (byte)sum;
        }

        private static string Normalize(string? input)
        {
            // Map the display glyphs to their ASCII stand-ins BEFORE NFKC normalization:
            // NFKC decomposes '…' (U+2026) into three ASCII dots, which would corrupt the
            // password's length if it ran first.
            var mapped = (input ?? "")
                .Replace('♂', '#')
                .Replace('♀', '%')
                .Replace('…', '.')
                .Normalize(NormalizationForm.FormKC);

            var builder = new StringBuilder();
            foreach (var c in mapped)
            {
                // Note '-' is NOT separator noise here: it's a real character in the password
                // alphabet (CHARS), so only whitespace and quotes get stripped.
                if (char.IsWhiteSpace(c) || c == '\'' || c == '"')
                {
                    continue;
                }
                builder.Append(char.ToUpperInvariant(c));
            }
            return builder.ToString();
        }
    }
}
