using SkyEditor.Core.IO;
using SkyEditor.IO.FileSystem;
using SkyEditor.SaveEditor.Extensions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue
{
    public class RBStoredPokemon
    {
        public const int BitLength = 362;
        public const string MimeType = "application/x-rb-pokemon";

        public event EventHandler FileSaved;

        public RBStoredPokemon()
        {
            Unk1 = new BitBlock(21);
            Unk2 = new BitBlock(43);
        }

        public RBStoredPokemon(BitBlock bits)
        {
            Initialize(bits);
        }

        public async Task OpenFile(string filename, IFileSystem provider)
        {
            var toOpen = new BitBlockFile();
            await toOpen.OpenFile(filename, provider);

            // matix2267's convention adds 6 bits to the beginning of a file so that the name will be byte-aligned
            for (int i = 1; i <= 8 - (BitLength % 8); i++)
            {
                toOpen.Bits.Bits.RemoveAt(0);
            }

            Initialize(toOpen.Bits);
        }

        public async Task Save(string filename, IFileSystem provider)
        {
            var toSave = new BitBlockFile();

            // matix2267's convention adds 6 bits to the beginning of a file so that the name will be byte-aligned
            for (int i = 1; i <= 8 - (BitLength % 8); i++)
            {
                toSave.Bits.Bits.Add(false);
            }

            toSave.Bits.Bits.AddRange(GetStoredPokemonBits());
            await toSave.Save(filename, provider);
            FileSaved?.Invoke(this, new EventArgs());
        }

        public async Task Save(IFileSystem provider)
        {
            await Save(Filename, provider);
        }

        private void Initialize(BitBlock bits)
        {
            Level = bits.GetInt(0, 0, 7);
            ID = bits.GetInt(0, 7, 9);
            MetAt = bits.GetInt(0, 16, 7);
            Unk1 = bits.GetRange(23, 21);
            IQ = bits.GetInt(0, 44, 10);
            HP = bits.GetInt(0, 54, 10);
            Attack = bits.GetInt(0, 64, 8);
            SpAttack = bits.GetInt(0, 72, 8);
            Defense = bits.GetInt(0, 80, 8);
            SpDefense = bits.GetInt(0, 88, 8);
            Exp = bits.GetInt(0, 96, 24);
            Unk2 = bits.GetRange(120, 43);
            Attack1 = new RBAttack(bits.GetRange(163, RBAttack.BitLength));
            Attack2 = new RBAttack(bits.GetRange(183, RBAttack.BitLength));
            Attack3 = new RBAttack(bits.GetRange(203, RBAttack.BitLength));
            Attack4 = new RBAttack(bits.GetRange(223, RBAttack.BitLength));
            nameBits = bits.GetRange(243, 80);
        }

        public BitBlock GetStoredPokemonBits()
        {
            var bits = new BitBlock(BitLength);
            bits.SetInt(0, 0, 7, Level);
            bits.SetInt(0, 7, 9, ID);
            bits.SetInt(0, 16, 7, MetAt);
            bits.SetRange(23, 21, Unk1);
            bits.SetInt(0, 44, 10, IQ);
            bits.SetInt(0, 54, 10, HP);
            bits.SetInt(0, 64, 8, Attack);
            bits.SetInt(0, 72, 8, SpAttack);
            bits.SetInt(0, 80, 8, Defense);
            bits.SetInt(0, 88, 8, SpDefense);
            bits.SetInt(0, 96, 24, Exp);
            bits.SetRange(120, 43, Unk2);
            bits.SetRange(163, RBAttack.BitLength, Attack1.ToBitBlock());
            bits.SetRange(183, RBAttack.BitLength, Attack2.ToBitBlock());
            bits.SetRange(203, RBAttack.BitLength, Attack3.ToBitBlock());
            bits.SetRange(223, RBAttack.BitLength, Attack4.ToBitBlock());
            bits.SetRange(243, 80, nameBits);
            return bits;
        }

        public string Filename { get; set; }
        public int Level { get; set; }
        public int ID { get; set; }
        public int MetAt { get; set; }
        public BitBlock Unk1 { get; set; }

        /// <summary>
        /// The dungeon floor this Pokemon was met/recruited on, paired with <see cref="MetAt"/>
        /// (the dungeon ID). Mirrors the decomp's <c>DungeonLocation</c> struct
        /// (<c>include/structs/str_dungeon_location.h</c>: <c>{ id (7 bits), floor (7 bits) }</c>,
        /// written together by <c>WriteDungeonLocationBits</c>). <see cref="MetAt"/> only ever
        /// captured the 7-bit <c>id</c> half; this exposes the 7-bit <c>floor</c> half, which
        /// lives in the first 7 bits of <see cref="Unk1"/> immediately after it.
        /// </summary>
        public int Floor
        {
            get => Unk1.GetInt(0, 0, 7);
            set => Unk1.SetInt(0, 0, 7, value);
        }

        /// <summary>
        /// Evolution history: the level this Pokemon was at when it evolved for the first time,
        /// or 0 if it never evolved while recruited. Bits 30-36 of the slot (the decomp's
        /// <c>unkC[0].level</c>, <c>ReadPoke1LevelBits</c>, src/pokemon_3.c:765), i.e.
        /// <see cref="Unk1"/> bits 7-13, right after <see cref="Floor"/>. Set by the evolution
        /// routine (<c>sub_808F798</c>, src/pokemon_evolution.c:227: the first zero entry
        /// receives the current level), so a Pokemon recruited already evolved legitimately has
        /// 0 here. Read by <c>GetEvolutionSequence</c> (src/pokemon.c:1201) to decide which
        /// pre-evolution learnsets Gulpin's move-remembering shop offers -- see
        /// <see cref="SecondEvolutionLevel"/> for the pairing quirk. Verified on the reference
        /// save: every never-evolved roster member reads 0/0.
        /// </summary>
        public int FirstEvolutionLevel
        {
            get => Unk1.GetInt(0, 7, 7);
            set => Unk1.SetInt(0, 7, 7, value);
        }

        /// <summary>
        /// Evolution history: the level at the second evolution, or 0 (bits 37-43 of the slot,
        /// <c>unkC[1].level</c>, <see cref="Unk1"/> bits 14-20). Note how the game consumes the
        /// pair: <c>GetEvolutionSequence</c> pairs the immediate pre-evolution with
        /// <see cref="FirstEvolutionLevel"/> and the pre-pre-evolution with this value, so for a
        /// two-stage chain Gulpin offers the middle form's moves only up to the first evolution
        /// level and the base form's moves up to the second -- as written in the decomp.
        /// </summary>
        public int SecondEvolutionLevel
        {
            get => Unk1.GetInt(0, 14, 7);
            set => Unk1.SetInt(0, 14, 7, value);
        }
        public int IQ { get; set; }
        public int HP { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int SpAttack { get; set; }
        public int SpDefense { get; set; }
        public int Exp { get; set; }
        public BitBlock Unk2 { get; set; }

        /// <summary>
        /// The held item's item ID, or 0 (ITEM_NOTHING) for none. Per the decomp's
        /// <c>WritePoke1Bits</c> (src/pokemon_3.c:617), the slot's bits 120-162 (this class's
        /// <see cref="Unk2"/>) are IQSkills(24) + tacticIndex(4) + heldItem via
        /// <c>WriteHeldItemBits</c>: id(8) + quantity(7). So the id sits at <see cref="Unk2"/>
        /// bits 28-35 and the quantity at 36-42. Verified against a real save: five roster
        /// Pokemon decode to plausible gear (Special Band, Oran Berry, Petrify Orb, Apple).
        /// </summary>
        public int HeldItemId
        {
            get => Unk2.GetInt(0, 28, 8);
            set => Unk2.SetInt(0, 28, 8, value);
        }

        /// <summary>
        /// The held item's stack count. Only meaningful for thrown/stackable items
        /// (Gravelerock and kin); the take-item flow only reads it for thrown items
        /// (friend_area_action_menu.c). The game never zeroes this field behind an
        /// id of 0: organic saves carry stale nonzero values here on itemless
        /// Pokemon (observed 61/118/2 on a real save), so it must round-trip
        /// verbatim rather than be normalized.
        /// </summary>
        public int HeldItemQuantity
        {
            get => Unk2.GetInt(0, 36, 7);
            set => Unk2.SetInt(0, 36, 7, value);
        }
        public RBAttack Attack1 { get; set; }
        public RBAttack Attack2 { get; set; }
        public RBAttack Attack3 { get; set; }
        public RBAttack Attack4 { get; set; }

        /// <summary>
        /// Backing store for <see cref="Name"/>: the raw 80-bit (10-byte) name buffer from the
        /// save. The game copies names in with a plain string copy, so bytes past the null
        /// terminator are whatever stale RAM the slot's struct held (a real save had
        /// "Doduo\0oon\0" left over from a longer string). Round-tripping those bytes verbatim
        /// keeps an untouched save byte-identical; the buffer is only regenerated (name +
        /// terminator + zero fill) when the name is actually changed.
        /// </summary>
        private BitBlock nameBits = new BitBlock(80);

        public string Name
        {
            get => nameBits.GetStringPMD(0, 0, 10);
            set
            {
                if (value == Name)
                {
                    return;
                }
                nameBits = new BitBlock(80);
                nameBits.SetStringPMD(0, 0, 10, value ?? string.Empty);
            }
        }

        /// <summary>
        /// The roster slot index (0-412) this Pokemon occupied when loaded from a save, or -1 if
        /// it hasn't been assigned a slot yet (e.g. newly constructed, not yet saved). The real
        /// save format is a fixed 413-slot array, not a compact list -- occupied slots can be
        /// scattered with gaps between them (confirmed against a real save file: occupied slots
        /// included indices like 54, 70-73, 95, 108-110... with empty slots interspersed). The
        /// game references specific slot indices elsewhere (active-team-member and team-leader
        /// indices, written right after this array by the decomp's SaveRecruitedPokemon) so
        /// preserving each existing Pokemon's original slot on save -- rather than compacting
        /// everything down to slots 0..N-1 -- avoids silently invalidating those references.
        /// </summary>
        public int SlotIndex { get; set; } = -1;


        public string GetDefaultExtension()
        {
            return "*.rbpkm";
        }

        public IEnumerable<string> GetSupportedExtensions()
        {
            return new string[] { GetDefaultExtension() };
        }

        public override string ToString()
        {
            if (ID > 0)
            {
                return string.Format(Resources.Language.SkyStoredPokemonToString, Name, Level, Lists.RBPokemon[ID]);
            }
            else
            {
                return Resources.Language.BlankPokemon;
            }
        }
    }
}
