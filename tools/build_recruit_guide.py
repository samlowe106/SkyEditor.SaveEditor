"""Regenerates RBRecruitGuideData.generated.cs from guide.md.

For each recruitable species in guide.md's main table, computes the exact stats a
legitimately recruited individual would have at its listed recruit level -- not the
level-1 base stats guide.md itself shows (see its "Stat-scaling caveat" section).

This needs a species' real per-level growth table (`lvmp###`), which is a resource
baked into the ROM's system file archive, AT-compressed, wrapped in a SIRO header.
None of that (the archive's directory table, the compressed bytes, or the SIRO
wrapper) is reproduced in the pret/pmd-red decomp checkout in any form -- only the
*code* that reads it is decompiled. So this script extracts it straight from a real
ROM dump instead:

Also rewrites guide.md's own main table in place, replacing its HP/Atk/SpAtk/Def/SpDef
column (which guide.md is explicit is only level-1 base stats, not real recruit-level
stats -- see its old "Stat-scaling caveat" section) with the same real numbers, plus a
new Exp column. Everything else about each row (location, level, friend area, recruit
rate, notes) is left untouched.

1. `data/system_sbin.s` in the decomp is a literal `incbin baserom.gba` of the whole
   system archive (`gSystemFileArchive`), including a `(nameAddr, dataAddr)` pair per
   file in ROM-address form -- so the byte offsets are just sitting there in the
   decomp source as plain text, no ROM-address computation needed beyond `addr -
   0x08000000` to turn a ROM address into a file offset.
2. Each `lvmp###` entry's `dataAddr` points to a 16-byte SIRO header
   (`include/decompress_sir.h`); bytes 4-7 of that header are a little-endian pointer
   to the real AT4P/AT3P-compressed payload.
3. `decompress_at()` below is a direct Python port of `src/decompress_at.c`'s
   `DecompressAT()` -- self-describing format, no need to know the output size
   upfront.
4. The decompressed bytes are a `LevelData[]` array (12 bytes/level: EXP required,
   HP/Atk/SpAtk/Def/SpDef gains -- see `include/structs/str_pokemon.h`). Level-1 base
   stats come from `data/monster/monster_data.json` (same source guide.md itself
   already uses); summing gains from level 2 up to the target level on top of that
   reproduces exactly what `LevelUp()` (src/dungeon_leveling.c) does in-game.

Requires a real Red Rescue Team ROM dump at ROM_PATH below (see repo README/TODO.md
for how this project verifies ROM legitimacy) and a local checkout of
https://github.com/pret/pmd-red (or a fork) at DECOMP_PATH.

Re-run this whenever guide.md's table changes.
"""

import json, re, struct, sys

DECOMP = "/home/sam/Documents/projects/RedRescueTeamRescued"
RES = "/home/sam/Documents/projects/Sky-Editor/SkyEditor.SaveEditor/SkyEditor.SaveEditor/Resources/en"
ROM = "/home/sam/Documents/projects/Sky-Editor/roms/Pokemon Mystery Dungeon - Red Rescue Team (U).gba"
GUIDE = "/home/sam/Documents/projects/Sky-Editor/SkyEditor.SaveEditor/guide.md"
OUT = "/home/sam/Documents/projects/Sky-Editor/SkyEditor.SaveEditor/SkyEditor.SaveEditor/MysteryDungeon/Rescue/RBRecruitGuideData.generated.cs"

# ---------------- AT decompression ----------------
def decompress_at(src: bytes) -> bytes:
    compressed_length = src[5] + (src[6] << 8)
    dst = bytearray()
    if src[0:4] not in (b'AT4P', b'AT3P'):
        raise ValueError(f"bad magic {src[0:4]!r}")
    idx_start = 0x12 if src[0:4] == b'AT4P' else 0x10
    if src[4] == ord('N'):
        return bytes(src[7:7+compressed_length])
    flags = [src[0x7+i] + 3 for i in range(9)]
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
                dst.append((c << 4) | ((c+1)&0xf)); dst.append((((c+1)&0xf) << 4) | ((c+1)&0xf))
            elif command == 0x1d:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | ((c-1)&0xf)); dst.append((c << 4) | c)
            elif command == 0x1c:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | c); dst.append((((c-1)&0xf) << 4) | c)
            elif command == 0x1b:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | c); dst.append((c << 4) | ((c-1)&0xf))
            elif command == 0x1a:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | ((c-1)&0xf)); dst.append((((c-1)&0xf) << 4) | ((c-1)&0xf))
            elif command == 0x19:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | ((c+1)&0xf)); dst.append((c << 4) | c)
            elif command == 0x18:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | c); dst.append((((c+1)&0xf) << 4) | c)
            elif command == 0x17:
                c = src[cur] & 0xf; cur += 1
                dst.append((c << 4) | c); dst.append((c << 4) | ((c+1)&0xf))
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
    while len(pairs) < 421:
        m1 = addr_re.search(all_lines[i]); m2 = addr_re.search(all_lines[i+1])
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

_growth_cache = {}
def growth_table(species_id):
    if species_id in _growth_cache:
        return _growth_cache[species_id]
    name_addr, siro_addr = lvmp_pairs[species_id - 1]
    name = read_cstr(rom, name_addr)
    assert name == f"lvmp{species_id:03d}", f"table mismatch for species {species_id}: {name}"
    siro_off = siro_addr - 0x08000000
    magic = rom[siro_off:siro_off+4]
    if magic in (b'SIR0', b'SIRO'):
        inner_addr = struct.unpack_from('<I', rom, siro_off + 4)[0]
        comp_off = inner_addr - 0x08000000
    else:
        comp_off = siro_off
    comp = rom[comp_off:comp_off+0x4000]
    raw = decompress_at(comp)
    n = len(raw) // 12
    entries = []
    for lvl_idx in range(n):
        chunk = raw[lvl_idx*12:(lvl_idx+1)*12]
        exp_required, gain_hp, ga0, ga1, gd0, gd1, fill = struct.unpack('<IHBBBBH', chunk)
        entries.append((exp_required, gain_hp, ga0, ga1, gd0, gd1))
    _growth_cache[species_id] = entries
    return entries

def final_stats(species_id, level):
    base = monster_data[species_id]
    hp = base['baseHP']
    atk, spatk = base['baseAtkSpAtk']
    de, spde = base['baseDefSpDef']
    exp = 0
    if level > 1 and species_id <= 421:
        table = growth_table(species_id)
        for lvl in range(2, level + 1):
            if lvl - 1 >= len(table):
                break
            exp_required, gain_hp, ga0, ga1, gd0, gd1 = table[lvl - 1]
            hp += gain_hp; atk += ga0; spatk += ga1; de += gd0; spde += gd1
            exp = exp_required
    return hp, atk, spatk, de, spde, exp

# ---------------- Name -> ID lookups ----------------
def load_ini(path):
    d = {}
    with open(path, encoding='utf-8-sig') as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            k, _, v = line.partition('=')
            d[int(k)] = v
    return d

rb_pokemon = load_ini(f"{RES}/RBPokemon.txt")
rb_locations = load_ini(f"{RES}/RBLocations.txt")

pokemon_name_to_id = {}
for k, v in rb_pokemon.items():
    name = v.strip()
    if name not in pokemon_name_to_id or k < pokemon_name_to_id[name]:
        pokemon_name_to_id[name] = k

location_name_to_id = {}
for k, v in rb_locations.items():
    name = v.strip()
    if name not in location_name_to_id or k < location_name_to_id[name]:
        location_name_to_id[name] = k

POKEMON_ALIASES = {
    "Nidoran (F)": "Nidoran♀",
    "Nidoran (M)": "Nidoran♂",
    "Unown": None,       # special-cased below
    "Unown (!)": None,
    "Unown (?)": None,
}
UNOWN_A_ID = 201
UNOWN_EXCLAIM_ID = 415
UNOWN_QUESTION_ID = 416

FRIEND_AREA_ALIASES = {
    "Aged Chamber O?": "AgedChamberOExclaim",
}

def friend_area_enum_name(raw):
    if raw in FRIEND_AREA_ALIASES:
        return FRIEND_AREA_ALIASES[raw]
    # PascalCase, strip non-alnum
    words = re.split(r'[^A-Za-z0-9]+', raw)
    return ''.join(w[:1].upper() + w[1:].lower() for w in words if w)

def resolve_species_id(name):
    if name == "Unown":
        return UNOWN_A_ID
    if name == "Unown (!)":
        return UNOWN_EXCLAIM_ID
    if name == "Unown (?)":
        return UNOWN_QUESTION_ID
    alias = POKEMON_ALIASES.get(name)
    lookup = alias if alias else name
    return pokemon_name_to_id.get(lookup)

def resolve_dungeon(location_text):
    # Special-case Latias: no clean "(NF)" in the text
    if location_text.startswith("Pitfall Valley (story event"):
        return "Pitfall Valley", 1
    m = re.match(r"^(.*?)\s*\((\d+)F(?:-\d+F)?\)", location_text)
    if not m:
        return None, None
    return m.group(1).strip(), int(m.group(2))

# ---------------- Parse guide.md table ----------------
rows = []
with open(GUIDE, encoding='utf-8') as f:
    in_table = False
    for line in f:
        line = line.rstrip('\n')
        if line.startswith("| Pokemon | Easiest Recruit Location"):
            in_table = True
            continue
        if in_table:
            if not line.startswith('|'):
                break
            if line.startswith('|---'):
                continue
            cells = [c.strip() for c in line.strip('|').split('|')]
            if len(cells) < 6:
                continue
            rows.append(cells)

print(f"Parsed {len(rows)} guide.md rows", file=sys.stderr)

entries = []
failures = []
for cells in rows:
    name, location, level_s, friend_area, recruit_rate, stats = cells[0], cells[1], cells[2], cells[3], cells[4], cells[5]
    species_id = resolve_species_id(name)
    if species_id is None:
        failures.append((name, "no species id"))
        continue
    dungeon_name, floor = resolve_dungeon(location)
    if dungeon_name is None:
        failures.append((name, f"can't parse location {location!r}"))
        continue
    dungeon_id = location_name_to_id.get(dungeon_name)
    if dungeon_id is None:
        failures.append((name, f"unknown dungeon {dungeon_name!r}"))
        continue
    try:
        level = int(level_s)
    except ValueError:
        failures.append((name, f"bad level {level_s!r}"))
        continue
    fa_enum = friend_area_enum_name(friend_area)

    hp, atk, spatk, de, spde, exp = final_stats(species_id, level)
    entries.append({
        "name": name, "species_id": species_id, "friend_area": fa_enum,
        "dungeon_id": dungeon_id, "dungeon_name": dungeon_name, "floor": floor,
        "level": level, "hp": hp, "atk": atk, "spatk": spatk, "de": de, "spde": spde, "exp": exp,
        "cells": cells,
    })

print(f"Resolved {len(entries)} entries, {len(failures)} failures", file=sys.stderr)
for name, reason in failures:
    print(f"  FAIL: {name}: {reason}", file=sys.stderr)

# ---------------- Emit C# ----------------
with open(OUT, 'w') as f:
    f.write("// AUTO-GENERATED by build_recruit_guide.py -- do not hand-edit.\n")
    f.write("// Source: guide.md, cross-referenced against monster_data.json and each species'\n")
    f.write("// real lvmp### growth table (extracted + AT-decompressed directly from the user's\n")
    f.write("// own ROM dump), so HP/Atk/SpAtk/Def/SpDef/Exp are the exact values a legitimately\n")
    f.write("// recruited Pokemon of that species would have at its typical recruit level.\n")
    f.write("using System.Collections.Generic;\n\n")
    f.write("namespace SkyEditor.SaveEditor.MysteryDungeon.Rescue\n{\n")
    f.write("    public static partial class RBRecruitGuide\n    {\n")
    f.write("        internal static readonly RecruitGuideEntry[] GeneratedEntries =\n        {\n")
    for e in entries:
        f.write(
            f'            new RecruitGuideEntry({e["species_id"]}, "{e["name"]}", RBFriendArea.{e["friend_area"]}, '
            f'{e["dungeon_id"]}, "{e["dungeon_name"]}", {e["floor"]}, {e["level"]}, '
            f'{e["hp"]}, {e["atk"]}, {e["spatk"]}, {e["de"]}, {e["spde"]}, {e["exp"]}),\n'
        )
    f.write("        };\n")
    f.write("    }\n}\n")

print(f"Wrote {len(entries)} entries to {OUT}", file=sys.stderr)

# ---------------- Rewrite guide.md's own table with real stats ----------------
entries_by_name = {e["name"]: e for e in entries}

OLD_HEADER = "| Pokemon | Easiest Recruit Location | Level | Friend Area | Recruit Rate | HP / Atk / SpAtk / Def / SpDef (base, at recruit) | Notes |"
NEW_HEADER = "| Pokemon | Easiest Recruit Location | Level | Friend Area | Recruit Rate | HP / Atk / SpAtk / Def / SpDef (at recruit) | Exp (at recruit) | Notes |"
OLD_SEP = "|---|---|---|---|---|---|---|"
NEW_SEP = "|---|---|---|---|---|---|---|---|"

OLD_CAVEAT = """## Stat-scaling caveat

Per-level stat growth in this game is **deterministic, not random** — no RNG is involved in computing a level-up's stat gains (`LevelUp()` in `src/dungeon_leveling.c`; the only randomness nearby is in *move* selection on level-up, not stats). However, the actual growth values are **not** a formula computed from base stats; each species has its own fixed 100-entry table (`GetLvlUpEntry()` in `src/pokemon.c`, loaded at runtime from a per-species `lvmp###` resource file baked into the ROM's ARM9 file archive) giving the exact HP/Attack/SpAttack/Defense/SpDefense gained at every level 1-100 and the EXP required to reach it. That table is **not present in this decomp checkout** in any decompiled or extracted form — it's still packed as an unextracted binary resource, so it could not be read from source alone in this pass.

Because of that, the HP/Attack/SpAttack/Defense/SpDefense column below gives each species' **level-1 base stats** (`baseHP` / `baseAtkSpAtk` / `baseDefSpDef` from `monster_data.json` — confirmed by reading `CreateLevel1Pokemon()` in `src/pokemon.c`, which sets a level-1 Pokemon's stats directly from these fields) alongside its typical recruit level from the dungeon spawn data. **A future bulk-importer should not fabricate final stats from a formula.** Instead, to get a recruited Pokemon's real stats at its recruit level, it should read that species' `lvmp###` growth table from the ROM (index `###` = the species' internal monster ID) and sum the `gainHP` / `gainAtt[0..1]` / `gainDef[0..1]` fields for levels 2 through the target level on top of the level-1 base — this exactly reproduces what the game itself does, with no guesswork."""

NEW_CAVEAT = """## Stat scaling: resolved, real per-level growth tables now used

Per-level stat growth in this game is **deterministic, not random** — no RNG is involved in computing a level-up's stat gains (`LevelUp()` in `src/dungeon_leveling.c`; the only randomness nearby is in *move* selection on level-up, not stats). The actual growth values are **not** a formula computed from base stats; each species has its own fixed 100-entry table (`GetLvlUpEntry()` in `src/pokemon.c`) giving the exact HP/Attack/SpAttack/Defense/SpDefense gained at every level 1-100 and the EXP required to reach it, loaded at runtime from a per-species `lvmp###` resource file. That table isn't reproduced anywhere in the pret/pmd-red decomp checkout — only the code that reads it is decompiled — so `tools/build_recruit_guide.py` pulls it directly from a real ROM dump instead: `data/system_sbin.s` in the decomp is a literal `incbin baserom.gba` of the ROM's entire system file archive, complete with a `(nameAddr, dataAddr)` pair per resource file in plain-text ROM-address form, so no address computation or archive-format reverse-engineering was actually needed beyond following what the decomp already spells out. The compressed payload behind each pointer was decompressed with a direct port of `src/decompress_at.c`'s algorithm.

The HP / Atk / SpAtk / Def / SpDef and Exp columns below are the **real stats and EXP total** a legitimately recruited individual of that species would have at its listed recruit level (level-1 base stats from `monster_data.json`, plus that species' real per-level growth summed from level 2 up to the recruit level) — not an approximation, and not fabricated from a formula. This is also what `SkyEditor.SaveEditor`'s `RBRecruitGuide` class and the save editor's friend-area "+ Recruit" picker use directly."""

with open(GUIDE, encoding='utf-8') as f:
    guide_lines = f.readlines()

out_lines = []
i = 0
n = len(guide_lines)
guide_failures = 0
while i < n:
    line = guide_lines[i]
    stripped = line.rstrip('\n')
    if stripped == OLD_HEADER:
        out_lines.append(NEW_HEADER + "\n")
        i += 1
        if guide_lines[i].rstrip('\n') == OLD_SEP:
            out_lines.append(NEW_SEP + "\n")
            i += 1
        while i < n and guide_lines[i].startswith('|'):
            row = guide_lines[i].rstrip('\n')
            cells = [c.strip() for c in row.strip('|').split('|')]
            name = cells[0]
            entry = entries_by_name.get(name)
            if entry:
                new_stats = f'{entry["hp"]} / {entry["atk"]} / {entry["spatk"]} / {entry["de"]} / {entry["spde"]}'
                new_exp = str(entry["exp"])
                new_cells = cells[:5] + [new_stats, new_exp] + cells[6:]
            else:
                guide_failures += 1
                new_cells = cells[:5] + [cells[5], "?"] + cells[6:]
            out_lines.append("| " + " | ".join(new_cells) + " |\n")
            i += 1
        continue
    out_lines.append(line)
    i += 1

guide_text = "".join(out_lines)
if OLD_CAVEAT not in guide_text:
    print("WARNING: old caveat text not found verbatim, leaving guide.md's caveat section untouched", file=sys.stderr)
else:
    guide_text = guide_text.replace(OLD_CAVEAT, NEW_CAVEAT)

with open(GUIDE, 'w', encoding='utf-8') as f:
    f.write(guide_text)

print(f"Rewrote guide.md's table ({guide_failures} rows left with unresolved '?' stats)", file=sys.stderr)
