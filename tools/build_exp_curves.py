"""Extracts every species' full level 1-100 EXP curve (cumulative EXP required to reach
each level) directly from a real ROM dump, using the exact same lvmp### extraction
pipeline as build_recruit_guide.py (see that script's module docstring for the full
explanation of the ROM archive/SIRO/AT-compression format).

Writes a compact binary resource (ExpCurves.bin): for species 1..421, 100 little-endian
uint32s (one per level 1-100, 0 for levels the species has no data for). Consumed by
SkyEditor.SaveEditor.Gui's ExpCurves.cs to show "EXP needed for level N" in the roster
detail pane, using the game's real per-species numbers rather than a guessed formula --
this game's growth data is a fixed per-species table, not a shared curve formula (see
build_recruit_guide.py's comments).
"""

import re, struct

DECOMP = "/home/sam/Documents/projects/RedRescueTeamRescued"
ROM = "/home/sam/Documents/projects/Sky-Editor/roms/Pokemon Mystery Dungeon - Red Rescue Team (U).gba"
OUT = "/home/sam/Documents/projects/Sky-Editor/SkyEditor.SaveEditor/SkyEditor.SaveEditor.Gui/ExpCurves.bin"

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
    comp = rom[comp_off:comp_off + 0x4000]
    raw = decompress_at(comp)
    n = len(raw) // 12
    entries = []
    for lvl_idx in range(n):
        chunk = raw[lvl_idx * 12:(lvl_idx + 1) * 12]
        exp_required, gain_hp, ga0, ga1, gd0, gd1, fill = struct.unpack('<IHBBBBH', chunk)
        entries.append(exp_required)
    return entries


out = bytearray()
zero_species = 0
for species_id in range(1, NUM_SPECIES + 1):
    table = growth_table(species_id)
    if not table:
        zero_species += 1
    for level in range(1, MAX_LEVEL + 1):
        idx = level - 1
        value = table[idx] if idx < len(table) else 0
        out += struct.pack('<I', value)

with open(OUT, 'wb') as f:
    f.write(out)

print(f"Wrote {OUT}: {len(out)} bytes ({NUM_SPECIES} species x {MAX_LEVEL} levels x 4 bytes)")
print(f"Species with empty tables: {zero_species}")

# Sanity spot-check: Charmander (species 4) at level 30 should match guide.md/RBRecruitGuideData's
# already-verified Exp value of 112290 for that exact species/level pair.
charmander = growth_table(4)
print(f"Charmander level 30 cumulative exp: {charmander[29]} (expect 112290)")
