"""Extracts every species' full growth table (level 1-100: cumulative EXP required plus
the exact HP/Atk/SpAtk/Def/SpDef gained on reaching each level) directly from a real ROM
dump, plus each species' level-1 base stats from the decomp's monster_data.json, into one
binary resource embedded in the SkyEditor.SaveEditor library (RBGrowthTables.bin, read by
RBGrowthTables.cs).

Same lvmp### pipeline as build_recruit_guide.py (see its docstring for the archive/SIRO/AT
format): data/system_sbin.s in the decomp lists a (name, data) ROM-address pair per lvmp
file; the payload is AT-compressed; decompressed, it is n x 12-byte rows of
  u32 expRequired, u16 gainHP, u8 gainAtt[2], u8 gainDef[2], u16 padding
indexed by (level - 1). Level-up stat growth in this game is fully deterministic: LevelUp
(src/dungeon_leveling.c) just adds row L when reaching level L (HP capped at 999, the
others at 255), and level-down subtracts row L when leaving level L (floored at 1). No RNG
touches stats; only which move gets learned is random.

This supersedes build_exp_curves.py / the GUI-only ExpCurves.bin, which carried only the
EXP column.

Output layout (little-endian):
  header: "RBGT" + u16 speciesCount(421) + u16 maxLevel(100)
  base stats, species 1..421 (index = species - 1): u16 hp, u8 atk, u8 spatk, u8 def, u8 spdef  (6 B)
  growth rows, species 1..421 x levels 1..100: u32 exp, u16 hp, u8 atk, u8 spatk, u8 def, u8 spdef (10 B)
Rows past a species' table length (and species with no table) are all zero.

Verification: for every RecruitGuideEntry in RBRecruitGuideData.generated.cs (which was
built independently by build_recruit_guide.py and spot-checked against the real save),
base + sum of gains for levels 2..L must reproduce its HP/Atk/SpAtk/Def/SpDef/Exp exactly.
RBGrowthTablesTests re-runs that check in C#.
"""

import json, re, struct

DECOMP = "/home/sam/Documents/projects/pokemon/RedRescueTeamRescued"
ROM = "/home/sam/Documents/projects/pokemon/Sky-Editor/roms/Pokemon Mystery Dungeon - Red Rescue Team (U).gba"
OUT = "/home/sam/Documents/projects/pokemon/Sky-Editor/SkyEditor.SaveEditor/SkyEditor.SaveEditor/Resources/RBGrowthTables.bin"
GUIDE = "/home/sam/Documents/projects/pokemon/Sky-Editor/SkyEditor.SaveEditor/SkyEditor.SaveEditor/MysteryDungeon/Rescue/RBRecruitGuideData.generated.cs"

NUM_SPECIES = 421
MAX_LEVEL = 100


def decompress_at(src: bytes) -> bytes:
    compressed_length = src[5] + (src[6] << 8)
    dst = bytearray()
    if src[0:4] not in (b'AT4P', b'AT3P'):
        raise ValueError(f"bad magic {src[0:4]!r}")
    idx_start = 0x12 if src[0:4] == b'AT4P' else 0x10
    if src[4] == ord('N'):
        return bytes(src[7:7 + compressed_length])
    flags = [src[0x7 + i] + 3 for i in range(9)]
    cur = idx_start
    cmd_bit = 8
    current_byte = 0
    while cur < compressed_length:
        if cmd_bit == 8:
            current_byte = src[cur]; cur += 1; cmd_bit = 0
        if (current_byte & 0x80) == 0:
            command = (src[cur] >> 4) + 3
            tmp = (src[cur] & 0xf) << 8
            for i, f in enumerate(flags):
                if command == f:
                    command = 0x1f - i
            if command == 0x1f:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | c); dst.append((c << 4) | c)
            elif command == 0x1e:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | ((c + 1) & 0xf)); dst.append((((c + 1) & 0xf) << 4) | ((c + 1) & 0xf))
            elif command == 0x1d:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | ((c - 1) & 0xf)); dst.append((c << 4) | c)
            elif command == 0x1c:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | c); dst.append((((c - 1) & 0xf) << 4) | c)
            elif command == 0x1b:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | c); dst.append((c << 4) | ((c - 1) & 0xf))
            elif command == 0x1a:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | ((c - 1) & 0xf)); dst.append((((c - 1) & 0xf) << 4) | ((c - 1) & 0xf))
            elif command == 0x19:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | ((c + 1) & 0xf)); dst.append((c << 4) | c)
            elif command == 0x18:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | c); dst.append((((c + 1) & 0xf) << 4) | c)
            elif command == 0x17:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | c); dst.append((c << 4) | ((c + 1) & 0xf))
            else:
                cur += 1
                tmp += src[cur]; cur += 1
                tmp += len(dst) - 0x1000
                for _ in range(command):
                    dst.append(dst[tmp]); tmp += 1
        else:
            dst.append(src[cur]); cur += 1
        cmd_bit += 1
        current_byte = (current_byte << 1) & 0xFF
    return bytes(dst)


def parse_lvmp_table():
    with open(f"{DECOMP}/data/system_sbin.s") as f:
        all_lines = f.readlines()
    start_idx = next(i for i, l in enumerate(all_lines) if "Pointer to lvmp001" in l)
    addr_re = re.compile(r"gUnknown_([0-9A-Fa-f]+)")
    pairs = []
    i = start_idx
    while len(pairs) < NUM_SPECIES:
        m1 = addr_re.search(all_lines[i]); m2 = addr_re.search(all_lines[i + 1])
        pairs.append((int(m1.group(1), 16), int(m2.group(1), 16)))
        i += 2
    return pairs


def read_cstr(rom, addr):
    off = addr - 0x08000000
    end = rom.index(b'\0', off)
    return rom[off:end].decode('ascii')


with open(ROM, 'rb') as f:
    rom = f.read()
lvmp_pairs = parse_lvmp_table()
monster_data = json.load(open(f"{DECOMP}/data/monster/monster_data.json"))


def growth_table(species_id):
    name_addr, siro_addr = lvmp_pairs[species_id - 1]
    name = read_cstr(rom, name_addr)
    assert name == f"lvmp{species_id:03d}", f"table mismatch for species {species_id}: {name}"
    siro_off = siro_addr - 0x08000000
    magic = rom[siro_off:siro_off + 4]
    if magic in (b'SIR0', b'SIRO'):
        inner_addr = struct.unpack_from('<I', rom, siro_off + 4)[0]
        comp_off = inner_addr - 0x08000000
    else:
        comp_off = siro_off
    raw = decompress_at(rom[comp_off:comp_off + 0x4000])
    rows = []
    for lvl_idx in range(len(raw) // 12):
        exp_required, gain_hp, ga0, ga1, gd0, gd1, _fill = struct.unpack_from('<IHBBBBH', raw, lvl_idx * 12)
        rows.append((exp_required, gain_hp, ga0, ga1, gd0, gd1))
    return rows


def base_stats(species_id):
    base = monster_data[species_id]
    atk, spatk = base['baseAtkSpAtk']
    de, spde = base['baseDefSpDef']
    return base['baseHP'], atk, spatk, de, spde


out = bytearray(b"RBGT" + struct.pack('<HH', NUM_SPECIES, MAX_LEVEL))
for species in range(1, NUM_SPECIES + 1):
    out += struct.pack('<HBBBB', *base_stats(species))

tables = {}
empty = 0
for species in range(1, NUM_SPECIES + 1):
    rows = growth_table(species)
    tables[species] = rows
    if not rows:
        empty += 1
    for level in range(1, MAX_LEVEL + 1):
        row = rows[level - 1] if level - 1 < len(rows) else (0, 0, 0, 0, 0, 0)
        out += struct.pack('<IHBBBB', *row)

with open(OUT, 'wb') as f:
    f.write(out)
print(f"Wrote {OUT}: {len(out)} bytes ({empty} species with empty tables)")

# Level-1 rows should carry no gains (base stats come from monster_data, not the table).
nonzero_l1 = [s for s, rows in tables.items() if rows and any(rows[0][1:])]
print(f"Species whose level-1 row has nonzero gains: {nonzero_l1}")

# Cross-check every recruit guide entry: base + sum(gains 2..L) must equal the entry exactly.
guide_re = re.compile(r'new RecruitGuideEntry\((\d+), "[^"]*", RBFriendArea\.\w+, \d+, "[^"]*", \d+, (\d+), (\d+), (\d+), (\d+), (\d+), (\d+), (\d+)\)')
checked = mismatches = 0
for m in guide_re.finditer(open(GUIDE).read()):
    species, level, hp, atk, spatk, de, spde, exp = map(int, m.groups())
    b_hp, b_atk, b_spatk, b_de, b_spde = base_stats(species)
    e = 0
    for lvl in range(2, level + 1):
        if lvl - 1 >= len(tables[species]):
            break
        row = tables[species][lvl - 1]
        e = row[0]; b_hp += row[1]; b_atk += row[2]; b_spatk += row[3]; b_de += row[4]; b_spde += row[5]
    checked += 1
    if (b_hp, b_atk, b_spatk, b_de, b_spde, e) != (hp, atk, spatk, de, spde, exp):
        mismatches += 1
        print(f"MISMATCH species {species} Lv.{level}: table gives {(b_hp, b_atk, b_spatk, b_de, b_spde, e)}, guide has {(hp, atk, spatk, de, spde, exp)}")
print(f"Recruit guide cross-check: {checked} entries, {mismatches} mismatches")
