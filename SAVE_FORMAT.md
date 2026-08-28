# Red/Blue Rescue Team save format

A map of the entire battery-backed save data Pokemon Mystery Dungeon: Red/Blue Rescue Team
reads and writes, built by cross-referencing the pret/pmd-red decomp
(`../RedRescueTeamRescued`, a fork of `pret/pmd-red`) against the real save bytes in
`Tests/SkyEditor.SaveEditor.Tests/Resources/BRT.sav`, and against `RBSave.cs`'s
already-shipped offsets. Every section below is tagged with a confidence level:

- **VERIFIED** — decoded against real save bytes and the decoded values are plausible/correct
  (matched known state, landed in valid ranges, or both a decomp-derived offset and an
  independently pre-existing offset agree).
- **SOURCED** — boundaries and sizes read directly from decomp source (`save.c`'s literal
  write order and buffer-size arguments), not independently verified against real bytes, but
  decomp source is about as authoritative as it gets.
- **UNKNOWN** — a region's existence and size are known, but its internal fields are not.

Byte offsets below are absolute offsets into the save file (`= struct offsets in the decomp's
`UnkStruct_sub_8011DAC`, confirmed with zero adjustment needed — see "Why file offset == struct
offset" below).

## Physical media size: 128KB (0x20000 bytes) — VERIFIED

`BRT.sav` in this repo is 512KB (524288 bytes), but only the first 128KB (0x20000) contains
real data — bytes `[0x20000, 0x40000)` are all `0x00` and `[0x40000, 0x80000)` are all `0xFF`,
with no other byte values anywhere past 0x20000. The extra padding is an artifact of whatever
tool produced this dump, not part of the game's actual save format. A real RRT/BRT cartridge
uses a 128KB flash chip, addressed by the game in 4KB (0x1000-byte) sectors (see
`src/flash.c:ReadFlashData`/`WriteFlashData`, and `src/save.c:sub_8011CA8`, which advances a
sector counter by `(size + 0xFFF) / 0x1000` after each `WriteSaveSector`/`ReadSaveSector` call).

## Top-level layout — VERIFIED

| Range | Size | Contents | Confidence |
|---|---|---|---|
| `0x00000`–`0x057D4` | 22484 B | Primary player save | VERIFIED |
| `0x057D4`–`0x06000` | 2092 B | Padding to sector boundary (all `0xFF`) | VERIFIED |
| `0x06000`–`0x0B7D4` | 22484 B | Backup player save (byte-identical mirror of primary) | VERIFIED |
| `0x0B7D4`–`0x0C000` | 2092 B | Padding to sector boundary (all `0xFF`) | VERIFIED |
| `0x0C000`–`0x10000` | 16384 B | **Unused gap** (sectors 12–15, all `0xFF` in the test save) | VERIFIED |
| `0x10000`–`0x20000` | 65536 B | Quicksave / mid-dungeon suspend data | SOURCED |

The primary/backup pair and the 16KB gap are directly confirmed: `WriteSavetoPak`
(`src/save.c:310`) calls `WriteSaveSector` twice back-to-back with
`sizeof(struct UnkStruct_sub_8011DAC)` = `0x57D4` bytes each time, and `0x57D4` bytes rounds up
to 6 sectors (`0x6000`), so the second call lands exactly at sector 6 = byte `0x6000` — matching
`RBSave.cs`'s pre-existing `BackupSaveStart => 0x6000` exactly. `WriteQuickSave`
(`src/quick_save_write.c:27`) writes the dungeon-suspend blob starting at the literal constant
sector 16 (`stack_1 = 16`) = byte `0x10000`, confirmed against real data: bytes
`[0x10000, 0x14000)` contain genuine non-padding data in `BRT.sav`, while `[0x0C000, 0x10000)`
is uniformly `0xFF`.

### Why file offset == struct offset

`CalculateChecksum(src, size)` (`src/save.c:69`) writes its result to the *first 4 bytes of
whatever buffer it's given* (`*(u32 *)out = checksum`), and `WriteSaveSector` calls it directly
on the `UnkStruct_sub_8011DAC` pointer before writing to flash. So the struct's own
`fill000[4]` field (misleadingly named — the decomp authors apparently didn't know its
purpose) *is* the real checksum, at file byte 0. This was confirmed two independent ways:
`RBSave.cs`'s pre-existing, already-tested `StoredPokemonOffset` computes to exactly
`0x448 * 8` bits — matching the struct's `unk448` field name exactly — and the checksum
coverage length in `CalculateChecksum`'s loop (`size/4 - 1` four-byte words starting at
`out[4]`) works out to exactly `sizeof(struct) - 4` bytes for `size = 0x57D4`, matching
`RBSave.cs`'s pre-existing `ChecksumEnd => 0x57D0` when that parameter is read as a *length*
rather than an absolute end offset.

## Primary/backup save header (`UnkStruct_sub_8011DAC`, `include/save.h:30`)

| Offset | Size | Field | Confidence |
|---|---|---|---|
| `0x000` | 4 B | Real checksum (sum of the rest of the struct, see above) | VERIFIED |
| `0x004` | 0x400 B | `unk004` — "global script vars" (`SaveGlobalScriptVars`) | UNKNOWN |
| `0x404` | 0x10 B | `gameInternalName` (`"POKE_DUNGEON__05"`) | SOURCED |
| `0x414` | 4 B | `checksum` — fixed sentinel `0x5071412`, not a real checksum | SOURCED |
| `0x418` | 4 B | `unk418` | UNKNOWN |
| `0x41C` | 4 B | `unk41C` | UNKNOWN |
| `0x420` | 4 B | `RngState` | SOURCED |
| `0x424` | 4 B | `savedRecruitedPokemon` — end-of-stream bit phase of the RecruitedPokemon section (see below) | VERIFIED |
| `0x428` | 4 B | `unk428` — end-of-stream bit phase of SavePoke2s | SOURCED |
| `0x42C` | 4 B | `fill42C` (unused padding) | SOURCED |
| `0x430` | 4 B | `savedTeamInventory` — end-of-stream bit phase of TeamInventory | SOURCED |
| `0x434` | 4 B | `savedRescueTeamInfo` — end-of-stream bit phase of RescueTeamInfo | SOURCED |
| `0x438` | 4 B | `savedFriendAreas` — end-of-stream bit phase of FriendAreas | SOURCED |
| `0x43C` | 4 B | `unk43C` — end-of-stream bit phase of AdventureData | SOURCED |
| `0x440` | 4 B | `unk440` — end-of-stream bit phase of the unidentified 0x594 section | SOURCED |
| `0x444` | 4 B | `savedMailInfo` — end-of-stream bit phase of MailInfo | SOURCED |
| `0x448` | 0x538C B | The "player data blob" — 8 sequential sections, see below | SOURCED |

The per-section fields at `0x424`-`0x444` were previously described here as byte lengths; they
are not. Each stores the section serializer's `DataSerializer.count` at end of stream, which is
the sub-byte bit cursor — i.e. **(total bits written) mod 8**, a weak consistency check the
loader compares against on read (`src/save.c:219-257`). Verified on the real save:
RecruitedPokemon uses exactly 134,807 bits (see its section below), and `0x424` holds
`134807 mod 8 = 7`.

### Global script variables (`0x004`, 0x400 bytes) — partially broken down, key entries VERIFIED

`unk004` is a verbatim byte image of `gScriptVarBuffer` (`SaveGlobalScriptVars`/
`RestoreGlobalScriptVars`, `src/event_flag.c:928-945`, plain `MemoryCopy8` both ways). Every
"global saved" script variable in `src/script_vars_info.c` whose storage group is 2 lives at
`file offset 0x004 + <its byte offset>`; the table below lists the ones identified and (where
noted) verified against the real save. On load the game validates only that `VERSION` still
equals its default 29 (`event_flag.c:942`) — there is no checksum beyond the whole-block one.

| File offset | Var (script_vars_info.c) | Type | Meaning | Real-save value |
|---|---|---|---|---|
| `0x004` | `VERSION` | s32 | Save-format sentinel, always 29; loader rejects the block otherwise | 29 ✓ |
| `0x008` | `CONDITION` | s32 | Scripted condition bits | 0 |
| `0x066` | `PARTNER_TALK_KIND` | s8 | Partner dialogue-personality index, derived from partner species via `sTalkKindTable` (`src/main_loops.c:96`) | 1 (= Squirtle) ✓ |
| `0x067` | `BASE_KIND` | s8 | Team Base theme, 0-15 — see "Team Base and flag" below. This is `RBSave.BaseType` (`BaseTypeOffset => 0x67 * 8`), whose old "starter type" description is now decomp-confirmed: `RBBaseTypes.txt`'s 16 names match `sBaseKindTable` exactly | 8 (= Charmander) ✓ |
| `0x068` | `BASE_LEVEL` | s8 | Base construction stage 0/1/2 (basic / under construction / final) | 2 ✓ |
| `0x069` | `FLAG_KIND` | s8 | Team flag design 0-15 (0 = species flag, 1-15 = Smeargle designs) | 0 ✓ |
| `0x06A` | `FLAG_KIND_CHANGE_REQUEST` | s8 | 1 = Smeargle redesign pending; consumed (FLAG_KIND+1 mod 16) on next overworld load | 0 |
| `0x072` | `SCENARIO_SELECT` | u8[2] | Script scenario selector | 0,0 |
| `0x074` | `SCENARIO_MAIN` | u8[2] | Main-story progress counter | 18,4 |
| `0x076`-`0x087` | `SCENARIO_SUB1`-`SUB9` | u8[2] each | Sub-scenario progress counters | — |
| `0x08D` | `PLAYER_KIND` | u8 | Player-kind script value | 2 |
| `0x08E` | `PARTNER1_KIND` | u8 | — | — |
| `0x08F` | `PARTNER2_KIND` | u8 | — | — |

Everything else in the 0x400 bytes (including the `SCENARIO_*` details and per-dungeon state
referenced elsewhere in this doc) remains unmapped and passes through `RBSave.cs` untouched,
except `BaseType` (byte `0x67`), which was already modeled and round-tripped.

## The player data blob (`0x448`, 0x538C = 22412 bytes reserved)

Built from `WriteSavetoPak` (`src/save.c:340-354`)'s literal, sequential write order. Each
section reserves a fixed maximum byte count (the buffer size argument) but the bitstream
serializer inside only uses however many bits it actually needs — so there is normally some
unused padding at the end of each section before the next one starts.

| Offset | Reserved size | Section | Decomp writer | Confidence |
|---|---|---|---|---|
| `0x0448` | 0x4650 B (17968 B) | RecruitedPokemon | `SaveRecruitedPokemon` (`src/pokemon_3.c:518`) | VERIFIED (see below) |
| `0x4A98` | 0x258 B (600 B) | SavePoke2s ("dungeon team") | `SavePoke2s` (`src/pokemon_3.c:669`) | SOURCED |
| `0x4CF0` | 0x1D8 B (472 B) | TeamInventory | `SaveTeamInventory` (`src/items.c:1220`) | VERIFIED |
| `0x4EC8` | 0x10 B (16 B) | RescueTeamInfo | `SaveRescueTeamInfo` (`src/rescue_team_info.c:114`) | VERIFIED |
| `0x4ED8` | 8 B | FriendAreas (bought/unlocked) | `SaveFriendAreas` (`src/friend_area.c:223`) | VERIFIED |
| `0x4EE0` | 0x100 B (256 B) | AdventureData (GameOptions+PlayTime+AdventureBits+ExclusivePokemonData) | `SaveAdventureData` (`src/adventure_save.c:9`) | **VERIFIED** |
| `0x4FE0` | 0x594 B (1428 B) | Unidentified — 32-entry array + one full Pokemon record + 33 u32s; plausibly friend-rescue state (see "Sections not broken down") | `sub_8095624` (`src/code_8094F88.c:369`) | UNKNOWN |
| `0x5574` | 0x221 B (545 B) | MailInfo (Wonder Mail / job board) | `SaveMailInfo` (`src/code_80958E8.c:1387`) | **VERIFIED** |

Section offsets above are computed by simple running addition of the reserved sizes
(`0x448 + 0x4650 = 0x4A98`, etc.) — all consistent with `RBSave.cs`'s pre-existing, independently
reverse-engineered `HeldItemOffset => 0x4CF0*8` landing exactly on the TeamInventory boundary,
and `TeamNameStart => 0x4EC8*8` landing exactly on the RescueTeamInfo boundary. Both matches are
exact (to the bit), which is why AdventureData's start (`0x4EE0`, right after FriendAreas) was
trusted enough to verify and then wire into code this session.

### RecruitedPokemon (`0x0448`) — VERIFIED end-to-end against a real save, matches `RBSave.StoredPokemon`

`SaveRecruitedPokemon` writes a **flat array of `NUM_MONSTERS` fixed slots**
(`NUM_MONSTERS` = `MONSTER_JIRACHI` = **413**, not indexed by species — despite the confusing
name, this is a roster of *slots*, matching `RBSave.cs`'s `StoredPokemonCount => 407 + 6 = 413`
exactly), each written via `WritePoke1Bits` (`src/pokemon_3.c:617`): level(7) + speciesNum(9) +
dungeonLocation(14) + two `WritePoke1LevelBits` + IQ(10) + HP(10) + atk(8) + spAtk(8) + def(8) +
spDef(8) + exp(24) + IQSkills(24) + tacticIndex(4) + heldItem + moves + name. This sums to
exactly **323 bits**, matching `RBSave.cs`'s `StoredPokemonLength => 323` exactly.
`RBStoredPokemon.cs`'s field names (`MetAt`, `Unk1`, `Unk2`) don't map 1:1 to the decomp's field
names/boundaries (e.g. the decomp's 14-bit `dungeonLocation` isn't the same shape as
`RBStoredPokemon.MetAt`'s 7 bits) — correctness of round-tripping isn't affected (both sides
agree on total width and the byte-for-byte content is preserved through `Unk1`/`Unk2`), but the
field *names* in `RBStoredPokemon.cs` shouldn't be trusted as decomp-accurate without further
work. Two pieces have since been decomposed properly out of the unknown regions: `Floor` (the
second half of `dungeonLocation`, first 7 bits of `Unk1`), the two evolution-history levels
(`unkC[0..1].level`, 7 bits each, `Unk1` bits 7-13 and 14-20 = slot bits 30-43, exposed as
`FirstEvolutionLevel`/`SecondEvolutionLevel`; `Unk1` is now fully decoded -- see "Evolution
history" below), and the held item (`Unk2` bits 28-42:
`WriteHeldItemBits`' id(8) + quantity(7), exposed as `HeldItemId`/`HeldItemQuantity` and verified
against a real save's five held-gear Pokemon). `Unk2` bits 0-27 are IQSkills(24) + tacticIndex(4),
still preserved raw. Note the held item, the Toolbox (`teamItems[20]`, `RBSave.HeldItems`), and
Kangaskhan storage are three *independent* stores in the engine — the game moves items between
them through menus, nothing requires them to mirror each other, so editing one never needs a
matching edit elsewhere.

**The roster is genuinely sparse, not a compact list — confirmed against a real save.** A
real, human-played save (`RRT.sav`) had exactly 20 recruited Pokemon occupying slots
`{54, 55, 70-73, 95, 108-110, 136, 186-188, 225, 317, 371-373, 398}` out of 413 — gaps
throughout, nothing packed toward slot 0. `RBSave.cs`'s original loading logic stopped at the
*first* empty slot it found (slot 0 on this save), so it silently returned an **empty roster**
instead of the real 20 — a serious bug, fixed this session by scanning every slot instead of
stopping early (see `RBStoredPokemon.SlotIndex` and `RBSave.LoadStoredPokemon`/
`SaveStoredPokemon`). It went undetected until now because the only prior test fixture
(`BRT.sav`, unknown provenance) happened to have its roster packed from slot 0.

Saving now preserves each Pokemon's original slot instead of compacting the list into
`0..N-1` on every write, because slot position may not be purely cosmetic — see the next
paragraph.

**The block's tail: team copies, on-team indices, and the team leader index — VERIFIED against
the real save.** After the 413 roster slots, `SaveRecruitedPokemon`/`RestoreRecruitedPokemon`
(`src/pokemon_3.c:518/577`) serialize three more pieces. All bit offsets below are relative to
the block start at file `0x448` (mirrored at `0x6448` in the backup block), LSB-first within
each byte like everything else in the stream:

| Bit offset | Size | Contents |
|---|---|---|
| 0 | 413 x 323 = 133,399 bits | The per-slot `WritePoke1Bits` records described above |
| 133,399 | 4 x 324 = 1,296 bits | `gRecruitedPokemonRef->team[4]` (`MAX_TEAM_MEMBERS` = 4): each entry is 1 exists/on-team bit + a full 323-bit `WritePoke1Bits` record — standalone Pokemon *copies*, not roster references |
| 134,695 | 6 x 16 = 96 bits | `sixMons`: s16 roster slot indices, -1 = empty. On load, each valid index gets `POKEMON_FLAG_ON_TEAM` set on that roster slot; on save, rebuilt by scanning the roster for that flag |
| 134,791 | 16 bits | `teamLeader`: s16 roster slot index, -1 = none. On load, that slot gets `isTeamLeader = TRUE`; on save, rebuilt by scanning |
| 134,807 | — | End of stream (`134807 mod 8 = 7` = the header's `savedRecruitedPokemon` check value) |

Note the asymmetry: the per-slot records do **not** store the on-team/leader flags at all
(`ReadPoke1Bits` explicitly zeroes them, `src/pokemon_3.c:643-644`); the roster's team
membership and leadership live *only* in these trailing slot indices, re-derived on every
in-game save. Decoded from the real save: `team[4]` = Charmander Lv.32, Squirtle Lv.27,
Duskull Lv.28, Duskull Lv.28 (the last assembled dungeon party, as standalone copies);
`sixMons` = [54, 225, -1, -1, -1, -1] (the hero's and partner's roster slots); `teamLeader` =
54 (the hero). Open question: the two Duskull exist in `team[]` but their roster slots are not
in `sixMons` — the exact relationship between `team[]` (copies) and the `ON_TEAM` indices
isn't fully pinned down yet.

**None of this tail is currently modeled in `RBSave.cs`** — the raw bits pass through a
load/save cycle unchanged. That is safe *only* because `SaveStoredPokemon` preserves each
Pokemon's original slot: the tail references roster Pokemon by slot number, so relocating
Pokemon (the old compacting behavior) would silently repoint the party and leader at different
Pokemon. Deleting a roster Pokemon whose slot appears in `sixMons`/`teamLeader` leaves a
dangling index (this is what the GUI's Farewell warning is about). Making "who is on the team"
and "who is the leader" editable means modeling these fields; the layout above is everything
needed to do it, and the `team[4]` copies would want to be kept consistent with their roster
counterparts when stats are edited.

### Evolution history (`unkC[0..1]`, slot bits 30-43) — VERIFIED

Each slot carries two 7-bit levels (`ReadPoke1LevelBits`, `src/pokemon_3.c:765`) right after
`dungeonLocation`. The evolution routine (`sub_808F798`, `src/pokemon_evolution.c:227`)
writes the Pokemon's current level into the first zero entry each time it evolves, so they
record the level at the first and second evolution; a Pokemon recruited already evolved
legitimately has 0/0 (nothing else in the game fills them). Every never-evolved member of
the real save reads 0/0.

Their only consumer is `GetEvolutionSequence` (`src/pokemon.c:1201`), which feeds Gulpin's
move-remembering shop (`sub_808E218`, `src/pokemon.c:1139`): the list of moves a Pokemon can
"remember" is recomputed purely from learnset tables -- current species' level-up moves up
to its level, plus each recorded pre-evolution's moves up to the stored evolution level,
minus IQ-gated ultimates, minus moves currently known. Nothing tracks what was actually
learned or forgotten. Quirk, as written in the decomp: the sequence pairs the immediate
pre-evolution with `unkC[0]` and the pre-pre-evolution with `unkC[1]`, which is the reverse
of the order the levels were recorded in for a two-stage chain (Bagon → Shelgon at L1,
Shelgon → Salamence at L2 gives Shelgon-moves-up-to-L1 and Bagon-moves-up-to-L2).

Editing consequence: a tool-added evolved form with 0/0 is exactly what a wild-recruited
one looks like, so it isn't a fingerprint; setting the levels only widens what Gulpin
offers. Both fields are editable (`RBStoredPokemon`, roster pane "Evolved at"), clamped to
0-100 in `ClampToGameLimits`.

### TeamInventory (`0x4CF0`) — VERIFIED

| Sub-offset (bits, from section start) | Field | Decomp writer | Maps to `RBSave.cs` |
|---|---|---|---|
| 0 | `teamItems[20]`, 23 bits each (flags(8)+quantity(7)+id(8)) | `WriteItemSlotBits` | `HeldItems` (fixed this session, see TODO.md) |
| 460 | `teamStorage[STORAGE_SIZE≈239]`, 10 bits each (quantity) | raw `WriteBits` | `StoredItems` |
| ~2850 | `kecleonShopItems[8]` + `kecleonWareItems[4]`, 15 bits each (id(8)+quantity(7)) | `WriteHeldItemBits` | **not modeled** |
| ~3030 | `teamMoney` (24 bits) + `teamSavings` (24 bits) | raw `WriteBits` | `HeldMoney` / `StoredMoney` |

`HeldItems` at 23-bit stride was verified directly against `BRT.sav` this session (see
`RBSaveDataTests.HeldItems_DecodeToKnownValuesFromRealSave`) — decoding produced a coherent
inventory (12 valid slots with sensible IDs/quantities, then 8 cleanly-empty slots); the
previous 33-bit stride produced incoherent garbage. `StoredItems`/`HeldMoney`/`StoredMoney`'s
existing offsets land within 10 bits (exactly one `teamStorage` slot width) of where raw decomp
arithmetic would put them — consistent with a minor, harmless indexing-convention difference
(e.g. 0- vs 1-based item IDs), not a structural bug. Kecleon shop items are not currently
exposed anywhere in `RBSave.cs`.

### FriendAreas (`0x4ED8`) — VERIFIED, fully wired into `RBSave.cs`

One bit per friend area (`FRIEND_AREA_COUNT` = 58, index 0 = `FRIEND_AREA_NONE` unused but
still a real bit) in the 58 low bits of the 8-byte (64-bit) reserved region, written by a
plain `for` loop in `SaveFriendAreas` (`src/friend_area.c:223`) with no gaps or padding
between bits. The offset itself is bracketed by two independently-verified real offsets —
`RescueTeamInfo` (containing the already-verified `TeamNameStart`) ends at `0x4ED8`, and
`0x4ED8 + 8 == 0x4EE0` lands exactly on the independently-verified `AdventureDataOffset` — so
this section's start isn't just decomp arithmetic, it's pinned on both sides by real data.
Wired into `RBSave.FriendAreasUnlocked`/`UnlockFriendArea()`; a real-save test
(`FriendAreasUnlocked_DecodesToPlausibleValues`) confirms index 0 is always false and at
least one area is unlocked on a save with 20 recruited Pokemon.

### AdventureData (`0x4EE0`) — VERIFIED, fully wired into `RBSave.cs`

See `RBOffsets.AdventureDataOffset` in `RBSave.cs` for the authoritative bit-level breakdown
(GameOptions 14 bits, PlayTime 32 bits, AdventureBits 1463 bits, `ExclusivePokemonData` 532
bits — 2041 bits total, fitting the reserved 256-byte/2048-bit buffer with 7 bits to spare).
AdventureBits' width is 1463: the eight scalar counters (137 bits) + `unk1C[14]`(448) +
`unk54[14]`(448) + `learnedMoves[13]`(416) + the trailing `WriteDungeonLocationBits` call
(id 7 + floor 7 = 14 bits) in `WriteAdventureBits`/`ReadAdventureBits` (`src/adventure_info.c`).
A session once re-tallied this without the dungeonLocation tail, got 1449, and "corrected" the
value, shifting every `ExclusivePokemonData` read/write 14 bits early; that is what manufactured
the "impossible cutscene flag pattern" chased in `FLAG_MYSTERY_INVESTIGATION.md` (now resolved).
Beware: the `numJoined`-decodes-to-18 and recruited-species-bitmap checks validate only
`AdventureDataOffset` (the section START); they are insensitive to this width and cannot be used
to confirm or refute it. The width itself is pinned by what decodes AFTER it: at 1463, on the
real fixture save, all 20 roster species read seen, cutscene flags form a contiguous story-order
run, and the 12 `ExclusivePokemonClaimed` bits equal the `in_rrt` new-game init pattern
(1,0,0,0,1,1,1,0,0,1,1,0); at 1449 all three fail.

### MailInfo (`0x5574`) — VERIFIED, wired into `RBSave.MailData`

Bit-exact layout from `SaveMailInfo`/`RestoreMailInfo` (`src/code_80958E8.c`), 4317 bits in the
0x221-byte buffer: 4 mailbox slots + 8 Pelipper board jobs + 8 accepted job slots (93-bit
`WonderMail` each, per `WriteWonderMailBits`: mailType 4 + missionType 3 + flavor 4 + client 9 +
target 9 + targetItem 8 + rewardType 4 + itemReward 8 + friendArea 6 + seed 24 + dungeon 7 +
floor 7), then 56 Pokemon News bits + 1 unknown bit, a 40-byte and a 120-byte unknown region
(preserved verbatim by `RBMailData`), then the 16-entry used-Wonder-Mail history FIFO (checksum 32
+ seed 24 + dungeon 7 + floor 7 each). Verified on a real save: accepted jobs decode as
byte-identical copies of their Pelipper-board originals, empty slots carry the exact
`ResetJobSlot`/`ResetMailboxSlot` sentinel (dungeon id 99), and each stored Wonder Mail's 93-bit
payload re-encodes (via `RBWonderMailPassword`, the same packing the 24-character password format
uses) to a checksum-valid password. The history is the game's "password already used" check
(`sub_8096F50`); it's a FIFO pushed on job completion (`sub_8096EEC`), so it only ever holds the
last 16 completed Wonder Mail jobs.

### `achievements` (32 bits, inside AdventureBits): deliberately left stale by this library, not a sign of corruption

`RecruitFromGuide`/`MarkBossRecruited` update `NumPokemonRecruited` and the per-species
recruited-flag bitmap (`unk1C`, via `SetRecruitedSpeciesFlag`) directly (bug 7 above), but never
write the separate 32-bit `achievements` field that sits right after `numEvolved` in the
`AdventureBitsBitLength` layout (`RBSave.cs`). If you inspect a tool-edited save's raw bytes, or
view the in-game Adventure Log screen from a save state saved *before* ever entering the town map
on this save, a newly-added legendary's "___ joined the team." line, or "All Pokémon joined the
team.", can still show up missing. **This is expected, self-correcting behavior, not evidence the
save was partially corrupted.** Confirmed by reading the actual game logic that owns this field:

- `UpdateAdventureAchievements()` (`src/adventure_info.c`) is the only code that ever sets
  `achievements` bits for recruiting. It's called unconditionally from `src/ground_main.c:303` on
  every `GROUND_ENTER`, i.e. every time the player is standing in the town/base. Since the data
  this library edits is the town-side main save, not the mid-dungeon quicksave (see "Quicksave"
  above), any "Continue" from a save this library wrote necessarily starts in town, so this
  function runs before the player can do anything else.
- That function is a pure, idempotent function of state this library *does* get right: it
  re-derives `unk1C` from the live roster itself (`gRecruitedPokemonRef->pokemon[i]`, a second,
  redundant confirmation of what `SetRecruitedSpeciesFlag` already wrote), then sets
  `AA_RECRUIT_<species>` for every legendary whose `unk1C` bit is set, and `AA_ALL_POKEMON_JOINED`
  if literally every species' bit is set. It only ever *sets* bits, never clears them, so it
  converges to the fully-correct state on the very first town entry and stays there. It isn't a
  "maybe" that requires further tool support: it always runs.
- The recruited-count line itself (`"{N} Pokémon joined the team."`, index 10 of
  `gAdventureLogText` in `src/strings.c`) reads `GetAdventureNumJoined()` **live**, every time the
  log screen is drawn (`DisplayAdventureLog`, `src/adventure_log.c`). It is not a frozen value
  captured when the achievement bit was first set. Since that bit is already set on any real save
  with prior recruit history, this line reflects `NumPokemonRecruited` correctly the instant the
  file is loaded, with no dependency on `UpdateAdventureAchievements` at all.
- The recruit scan has no relationship to the friend-area slot-range bug (bug 6 above). It walks
  `RecruitedMon.pokemon[NUM_MONSTERS]` (`include/structs/str_pokemon.h`), and `NUM_MONSTERS` is
  `#define NUM_MONSTERS MONSTER_JIRACHI` = **413** (`include/constants/monster.h`), i.e. the exact
  same 413 roster slots as `RBSave.StoredPokemon`, checked with a simple per-slot
  `PokemonExists`/level flag (`include/pokemon.h`). There is no friend-area-range logic anywhere
  in this path, so a tool-added Pokemon counts identically regardless of which slot or friend area
  it landed in.

One achievement genuinely does **not** self-heal and has no tool support: `AA_ALL_POKEMON_LEADERS`
("All Pokémon were made leaders.") depends on `unk54`, a separate bitmap only ever set by actually
switching to that species as the active team leader in-game (`sub_80978C8`). Nothing this library
does touches it, and nothing will make that line appear for a tool-added Pokemon without doing so
by hand in-game.

Not writing `achievements` directly is a deliberate simplification verified safe by the above, not
an oversight. The trade-off is a one-frame-of-"continue" cosmetic lag before the log fully
reflects tool-added recruits, not a data-integrity issue. Porting
`UpdateAdventureAchievements`'s logic into `PreSave` so the log is correct from byte zero is
possible if that lag is ever worth removing.

### Roster-derived state: the full sync matrix (audited against the decomp)

Everything the game keeps that is *about* the roster, and whether the tool must maintain it when
it adds or removes roster entries:

| State | Game re-derives it itself? | Tool action |
|---|---|---|
| `unk1C` recruited-species bitmap | Yes: `UpdateAdventureAchievements` ORs it from the live roster on every town entry and every in-game save | Also set eagerly (bug 7) so the log is right immediately; redundant but harmless |
| `learnedMoves` bitmap + `adventureMovesLearned` | Yes: same function ORs every roster member's current moves | Nothing needed; a tool-added moveset is counted on the game's next town entry |
| `achievements` | Yes: same function, set-only, idempotent | Deliberately untouched (see above) |
| `numJoined` ("Pokémon joined the team: N") | **No**: a pure counter incremented at recruit/evolve events, never recomputed | Synced by `PreSave` as an increment-only net diff of roster adds (matches the game's cumulative, never-decrementing semantics) |
| Monster **seen flags** (`ExclusivePokemonData.monSeenFlags`) | **No**: set only when the leader lands a kill or a Pokemon joins (`src/pokemon.c`), never recomputed | `PreSave` enforces the invariant "in roster implies seen" for every roster species. Without it, tool-added species stay invisible to Wigglytuff's friend-area shop, Wonder Mail client generation (`pokemon_mail.c`), and story NPC checks (`GetMonSeenFlag(MONSTER_HO_OH)` in `ground_script.c`) |
| `unk54` been-team-leader bitmap | No, but only `dungeon_main.c` sets it, when that species actually leads a dungeon entry | Correctly untouched: a tool-added recruit genuinely hasn't led |
| Friend area of the added species | No | Unlocked by `RecruitFromGuide`/`MarkBossRecruited` (bug 6); additionally `PreSave` enforces "occupied slot implies that slot's area is unlocked" for the whole roster (slot-based, via `RBFriendAreaCapacity.AreaForSlot`), so direct API adds that bypass those paths are corrected too. As a bonus, friend-area-gated bonus dungeons then open themselves on the next town entry (see RECRUIT_MECHANICS.md) |

### Sections not broken down further here

- **SavePoke2s** (`0x4A98`): 4 entries (`gRecruitedPokemonRef->dungeonTeam[4]`) — a
  dungeon-ready snapshot of the active party (adds belly, hidden power, tactic, held item slot,
  etc. on top of the RecruitedPokemon fields). Not modeled in `RBSave.cs`.
- **The 0x594 section** (`0x4FE0`): writer identified (`sub_8095624`, reader `sub_80954CC`,
  `src/code_8094F88.c:323/369`) and its shape now partially read: 32 records
  (`sub_8095774`/`sub_8095824` over `gUnknown_203B480[i]`), then a u32, then **one full Pokemon
  record** with the roster codec's fields in a slightly different order: flags(2) +
  isTeamLeader(1) + level(7) + dungeonLocation + species(9) + the rest as in `WritePoke1Bits`,
  then a u32 + 32 more u32s. The surrounding code touches rescue-mail client data (`gUnknown_203B480->clientSpecies`,
  `src/code_8094F88.c:278`), so this is plausibly the friend-rescue state (the rescuer/client
  Pokemon and the rescue job list). Purpose still unconfirmed; needs further digging before
  modeling.
- **MailInfo** (`0x5574`): Wonder Mail box + job board + Pelipper board + used-password
  history — since fully modeled (`RBSave.MailData`, see its VERIFIED section above); this
  bullet previously predated that work.
- **Quicksave** (`0x10000`–`0x20000`): the mid-dungeon suspend save, written by
  `dungeon_serializer.c` (partially explored earlier this session — e.g. the per-entity
  `bossFlag` scratch field lives here, not in the main save). Out of scope for save-editing use
  cases that only care about the overworld state; not broken down further in this document.

## Hero, partner, team leader, Team Base, and the team flag (verified against the decomp and the real save)

How the game answers "who is the protagonist?", "who leads the team?", and "what does my base
and flag look like?" when it loads a save. Documented with future editability in mind: every
piece below is a concrete, located field.

### Hero and partner identity: sentinel "met at" values, nothing else

There is no dedicated hero field anywhere in the persisted save. The hero and partner are
ordinary roster entries recognized by their 14-bit `dungeonLocation` ("met at") sentinels
(`include/constants/dungeon.h:73-74`, tested by `IsMonLeader`/`IsMonPartner`,
`include/pokemon.h:154-162`):

- `dungeonLocation.id == 64` (`DUNGEON_JOIN_LOCATION_LEADER`, displays as "???") — the hero.
- `dungeonLocation.id == 65` (`DUNGEON_JOIN_LOCATION_PARTNER`, displays as "Tiny Woods") — the
  partner.

On the real save: slot 54 (Charmander, MetAt=64) is the hero, slot 225 (Squirtle, MetAt=65)
is the partner — matching `RBStoredPokemon.MetAt`, so **the editor can already read and
rewrite hero/partner identity today**; "make X the hero" is just editing MetAt values (one
Pokemon per sentinel, and story scripts/evolution checks key off these, so exactly one of
each should exist). If both scans come up empty, `sub_8001064` (`src/main_loops.c:952`,
run by the new-game script flow right after the personality quiz) recreates them from the
quiz result via `CreateLeaderPartnerData` (`src/pokemon.c:71`): level 1, base stats, IQ 1,
level-1 moves, placed in a free slot of the species' friend-area range with the area
unlocked and the seen flag set. The quiz's `TeamBasicInfo` (starter/partner species + names,
`include/personality_test1.h:18`) is EWRAM-only state and is **not** part of the flash save;
after the intro the roster entries themselves are the only identity.

### Current team leader and active team: the roster block's trailing slot indices

Who you walk around as (changeable postgame at the Team Base) is a separate concept from the
hero. It is persisted as the 16-bit `teamLeader` roster slot index at the tail of the
RecruitedPokemon block, alongside the six 16-bit `ON_TEAM` indices and the `team[4]` copies —
full verified layout in the RecruitedPokemon section above. Making leadership/party membership
editable means modeling that tail; everything needed is documented there.

### Team Base: `BASE_KIND` x `BASE_LEVEL` (global script variables)

The base's overworld map is computed at load, not stored: `GetAdjustedGroundMap`
(`src/ground_map.c:365`) remaps `MAP_TEAM_BASE`/`MAP_TEAM_BASE_INSIDE` to
`base + BASE_KIND * TEAM_BASE_MAPS_PER_SPECIES + BASE_LEVEL`.

- `BASE_KIND` (file `0x067`, s8, = `RBSave.BaseType`): which of 16 species-themed bases, per
  `sBaseKindTable` (`src/main_loops.c:75`): 0 Pikachu, 1 Meowth, 2 Eevee, 3 Skitty,
  4 Squirtle, 5 Totodile, 6 Mudkip, 7 Psyduck, 8 Charmander, 9 Torchic, 10 Cyndaquil,
  11 Cubone, 12 Machop, 13 Bulbasaur, 14 Chikorita, 15 Treecko — exactly the editor's
  existing `RBBaseTypes.txt`. Set once from the starter species at quiz end (`sub_8001064`);
  never re-derived afterward, so an edited value sticks.
- `BASE_LEVEL` (file `0x068`, s8): construction stage — 0 basic, 1 under construction,
  2 final; advanced by story events (`src/event_flag.c:565` sets 2 and fires the
  `AA_TEAM_BASE_DONE` achievement).

Both are plain bytes in the global script-variable image (see the header section above for the
region), mirrored at `+0x6000` like everything else. `BASE_KIND` is already editable via
`RBSave.BaseType`; `BASE_LEVEL` would be a one-line addition next to it.

### Team flag: `FLAG_KIND` + the Smeargle redesign event

The flag outside the base is ground-object kind `0x1a`, resolved fresh on every map load
(`src/ground_object.c:290-304`):

- `BASE_LEVEL < 2` → generic flag (kind `0x1b`), regardless of `FLAG_KIND`.
- else `FLAG_KIND == 0` → the species-themed flag (`BASE_KIND + 0x1c`).
- else → Smeargle design `FLAG_KIND` (1-15), kind `FLAG_KIND + 0x2b`.

`FLAG_KIND` lives at file `0x069` (s8). The in-game way to change it is the Smeargle artist
event (`src/data/ground/ground_event_data.h:151`), which sets `FLAG_KIND_CHANGE_REQUEST`
(file `0x06A`) to 1; the next overworld load consumes it and increments `FLAG_KIND` mod 16
(`src/ground_main.c:494-501`), cycling back to the species flag at 0. An editor can just
write `FLAG_KIND` directly (0-15) and leave the request byte at 0.

## Write-path bugs found and fixed this session

All found by round-tripping a real save file and checking the *specific known values* it
should contain — not just checking that the checksum still validates, which none of these
bugs broke (a checksum only proves internal consistency, not correctness against the source
data).

1. **Held-item slot stride was 33 bits, should be 23** (`RBHeldItem`/`RBOffsets.HeldItemLength`).
   Fixed and reverted within this same session — see the TeamInventory section above.
2. **`StoredPokemon` loading stopped at the first empty roster slot** instead of scanning all
   413, silently returning an empty (or truncated) roster on any save whose occupied slots
   aren't packed from index 0 — which real saves aren't. Fixed by scanning every slot; see the
   RecruitedPokemon section above.
3. **The primary→backup save copy used byte-scale numbers where bit indices were expected.**
   `RBOffsets.BackupSaveStart` (`0x6000`) is a byte offset everywhere else in the codebase
   (e.g. `GetUInt(Offsets.BackupSaveStart, 0, 32)` for the backup checksum), but
   `RBSave.PreSave()`'s backup-copy line fed it directly into `BitBlock.GetRange`/`SetRange`,
   which operate in bits. The result: instead of copying the ~24576-byte primary save into the
   backup region, it copied a ~24572-*bit* (~3071-byte) slice of the primary save back onto
   itself at a shifted position, corrupting roughly file bytes `[3072, 24576)` — which is most
   of the RecruitedPokemon section — on *every single save*. Checksums still validated
   afterward because they're computed from whatever ended up in that range, not against an
   independent source of truth. Fixed by multiplying `BackupSaveStart` by 8 before using it as
   a bit offset. This is the most severe bug found this session: any prior save-and-write
   through this library would have silently destroyed a large chunk of the recruited-Pokemon
   roster, for any Pokemon in a roster slot at or past index ~49.
4. **`RBSave.ToByteArray()` called `PreSave()` explicitly, then called `base.ToByteArray()`**,
   which itself calls `PreSave()` again (virtual dispatch resolves it back to `RBSave`'s
   override) — the entire save-encoding pipeline ran twice on every `ToByteArray()` call.
   Harmless once bug 3 is fixed (each pass is idempotent), but wasteful; removed the redundant
   explicit call. The identical pattern (`PreSave(); return base.ToByteArray();`) exists in
   `TDSave.cs` and `SkySave.cs` too — not touched, since those games' save formats haven't been
   verified this session, but worth checking if that code is ever revisited.

5. **RETRACTED — this "fix" was itself the bug.** A session re-tallied `WriteAdventureBits`
   field-by-field, missed the trailing `WriteDungeonLocationBits` call (14 bits), concluded the
   width was 1449, and changed the correct 1463 to it. The cited evidence (`numJoined` decoding
   to the Adventure Log's "18") only anchors `AdventureDataOffset`, the section start, and is
   insensitive to the width, so it validated nothing about this change. The result was every
   `ExclusivePokemonData` read shifted 14 bits early: cutscene flag N read as flag N-14, which
   manufactured the "impossible flag pattern" in `FLAG_MYSTERY_INVESTIGATION.md`. Restored to
   1463, with the actual width-sensitive validation recorded in the AdventureData section above
   and in `RBSave.cs`.
6. **Friend area membership is determined by roster *slot index*, not species.** `sub_80923D4`/
   `GetFriendAreaCapacity` (`src/friend_area.c`) statically partition the entire 413-slot
   recruited-Pokemon roster into contiguous per-area ranges (`gFriendAreaSettings[].num_pokemon`,
   `src/dungeon_data.c`, in `FRIEND_AREA_*` order — see `RBFriendAreaCapacity`), and decide "does
   this Pokemon live in area X" purely by whether its slot falls in that area's range. Neither
   `RecruitFromGuide` nor `MarkBossRecruited` accounted for this before this session — both just
   appended new entries to the lowest globally-free slot, which almost never happens to fall
   inside the intended area's range. In-game this showed up exactly as reported: a Pokemon added
   "into" a friend area was visitable in the roster but didn't spawn there, and the area's
   occupant count didn't move. Fixed by placing new entries via
   `RBSave.FindFreeSlotInFriendArea`, which throws if the target area is already full rather than
   silently misplacing the Pokemon.
7. **The Adventure Log's recruited-count (`numJoined`) and per-species recruited-flag (`unk1C`)
   are separate, monotonic pieces of state the game increments once per recruit event**
   (`IncrementAdventureNumJoined`, `sub_80978C8` — `src/dungeon_mon_recruit.c` and others) — they
   are never recomputed from the roster's actual contents on load. Adding roster entries directly
   (as this library always has) left both permanently stale, which is why the Adventure Log kept
   showing an old "Pokemon recruited" count no matter how many were added through the tool. Fixed
   by bumping both in `RecruitFromGuide`/`MarkBossRecruited`; see `RBSave.NumPokemonRecruited`/
   `SetRecruitedSpeciesFlag`.

8. **The backup-block fallback mixed byte and bit offsets (and some loaders ignored it).** When
   the primary block's checksum is invalid and the backup's is valid, `Init` reads from the
   backup — but it passed `BackupSaveStart` (a byte offset, 0x6000) into loaders that add it to
   bit offsets, so every "fallback" read actually came from 0xC00 bytes into the file; and
   `LoadGeneral`'s money/points reads plus the stored-items read ignored the fallback entirely
   and read the corrupt primary. Worst case, PreSave would then have re-written that garbage
   under freshly valid checksums, destroying the intact backup. Fixed by making the base offset
   bit-valued and applying it in every loader; `Load_FallsBackToBackupBlock_WhenPrimaryIsCorrupt`
   corrupts 4KB of the primary roster and asserts the save loads and re-saves intact.
9. **Held-item loops ran to 50 slots; the bag has 20** (`INVENTORY_SIZE`,
   `include/constants/item.h` — 50 is Explorers' bag size). The save side zero-filled 30 phantom
   23-bit slots past the bag's end, running 690 bits into the storage-quantity array and
   silently wiping the stored quantities of roughly the first 68 item IDs on **every save the
   tool ever wrote**. Undetected because no test round-tripped storage contents; found by a
   storage round-trip probe during the integrity audit (a real save went from 42 stored stacks
   to 24). Guarded by `StoredItems_SurviveRoundTrip`.
10. **Storage quantities were capped at 1024; the field is 10 bits and the game's own cap is
    999** (`src/items.c:979`). A stack reaching the old cap wrote `1024 & 0x3FF = 0` and deleted
    itself; 1000-1023 were values the game can never produce. `PreSave` now also clamps money
    (99999 held / 9999999 stored, `MAX_TEAM_MONEY`/`MAX_TEAM_SAVINGS`), IQ (999), level
    (1-100 — 0 marks an empty roster slot), and max HP (999) to the game's own limits, so no
    edit can overflow a bitfield or produce a value organic play can't.

11. **Zeroing the held-item quantity behind an empty item id erased organic bytes.** The game
    never clears the 7-bit held-item quantity field when a Pokemon has no item: it serializes
    the RAM struct's leftover bytes, and every read of the field sits behind an id check. A
    real save carried stale quantities of 61, 118, and 2 on itemless Pokemon. `PreSave`'s
    clamp pass normalized these to 0, changing bytes the game itself preserves: a detectable
    tool fingerprint, though harmless to the game. Fixed by clamping the quantity to its field
    range only, never conditioning on the id. The GUI still starts a newly assigned item at
    quantity 0 so the stale bits don't surface as a phantom stack.
12. **Re-encoding Pokemon names zero-filled the buffer past the terminator, erasing organic
    garbage.** The game copies names with a plain string copy into a 10-byte buffer, so bytes
    past the null terminator are stale RAM (a real save had `Doduo\0oon\0`, the tail of a
    longer string, and a leftover `♂` glyph after `Weedle\0`). The loader decoded the name to
    a string and the saver re-encoded it with a zero-filled tail, so merely opening and saving
    scrubbed those bytes on four roster slots. Fixed by keeping the raw 80-bit name buffer and
    only regenerating it (name + terminator + zero fill) when the name is actually changed.

None of bugs 2-4 were caught by the test suite that existed before this session, because
nothing previously exercised a save/reload round-trip against real, non-trivial save data —
only checksum validity and isolated field reads. The lesson generalized to `TODO.md`: a fix
that only stops a crash or keeps a checksum valid isn't verified — decode the actual resulting
values and check them against something independently known to be true.

## Byte-level algebra of the save pipeline (verified)

`RBSaveAlgebraTests` pins down the pipeline's behavior as algebraic laws, checked byte-for-byte
against the real save. Serialization is a pure function of the in-memory model; edits are
last-writer-wins field writes; the only deliberate impurities are two monotone ratchets that
mimic the game's own irreversible bookkeeping. Concretely:

- **Identity**: `save(load(b)) == b` for an organic save `b`, all 131072 bytes, including the
  stale held-item quantities and name-buffer garbage above. This is the single strongest
  no-fingerprint guarantee the tool can make, and it caught bugs 11 and 12.
- **Idempotence**: resaving an already-saved file is a byte-level no-op.
- **Invertibility**: for edits with natural inverses (money, flags, friend areas, held items,
  Toolbox slots), edit-save-revert-save returns the original file exactly, even across a full
  reload between the edit and the revert.
- **Commutativity**: edits to disjoint fields produce identical bytes in either order, because
  serialization only sees the final model state. Order does matter for operations that allocate
  positions (Toolbox append order, roster slot assignment for new recruits): those produce
  differently-arranged but game-equivalent saves, the same way doing the actions in a different
  order in-game would.
- **Ratchets**: adding a roster Pokemon, saving, deleting it, and saving again restores every
  byte except the documented monotone state: `numJoined` ticks up once, and the species' seen
  flag and ever-recruited (`unk1C`) flag latch on. That matches the game: those are
  increment-only/set-only there too, and only `unk1C` is ever re-derived from the roster
  (`UpdateAdventureAchievements`, next town entry), so the game itself would clear that one
  after the undo while `numJoined` and the seen flag stay latched forever. The test asserts the
  full diff is confined to exactly those bit ranges plus checksums and the backup mirror.

So edits form a commutative monoid action on save states up to game-equivalence; it fails to be
a group action only at the ratchets, which is a property inherited from the game, not a tool
defect.

### The border: free, monotone, derived, and passthrough regions

Restricted to the right subset of coordinates the action really is a commutative group action;
the monoid behavior comes entirely from a separate, identifiable region. The modeled state
splits into four:

- **Free region** (a torsor under an abelian group): team name, held and stored money, Toolbox
  contents, storage quantities, roster membership itself (recruiting and "Say farewell" are
  both organic moves), and every per-Pokemon field (level, IQ, HP, stats, exp, moves, held
  item, name). Each coordinate is a torsor under its value group (flags under toggle are C2,
  bounded numerics under translation); disjoint coordinates commute exactly; every edit here is
  invertible, and any in-range value is a state organic play could have produced.
- **Monotone region** (an ordered commutative monoid): the join-semilattice part is the seen
  flags, the ever-recruited (`unk1C`) flags, cutscene/story/tutorial flags, and unlocked friend
  areas; the counter part is `numJoined`, rescue points, and play time. Organic play only moves
  up the order, and the tool's automatic updates (the ratchets) do the same. The tool *does*
  permit downward moves (clearing a flag, relocking an area): each lands on a state some
  shorter history could have produced, so it is not corruption per se, but it is a *rewind*
  the game itself can never perform, and a partial rewind can violate the cross-invariants
  below. The used-Wonder-Mail history sits slightly apart: organically it is an append-only
  16-entry FIFO (a sliding window, so not globally monotone), and the tool's removal reproduces
  the shift+sentinel shape the window itself uses.
- **Derived region** (a section of the state space, not degrees of freedom): the two checksums,
  the backup block (equal to the primary after every save, the same equality the game
  maintains), and the Adventure Log fields the game recomputes on town entry (`achievements`,
  `learnedMoves`, and the `unk1C` re-derivation). The tool recomputes the first two on every
  save and deliberately leaves the rest stale for the game to fix. Nothing here should ever be
  user-editable.
- **Passthrough region** (unmodeled): script variables (`SCENARIO_*`), dungeon open/conquered
  lists, the active party (`SavePoke2s`), team/leader slot indices, Kecleon shops, the 0x594
  section, the quicksave region, and residual in-slot garbage (name-buffer tails, stale
  held-item quantities). Preserved bit-for-bit; the identity law is the regression guard for
  this entire class. Two passthrough pieces have since been fully located and byte-verified,
  ready to move into the free region when editing is wanted: the team/leader slot indices
  (RecruitedPokemon tail layout above) and the base/flag script variables (`BASE_LEVEL`,
  `FLAG_KIND` — see "Hero, partner, team leader, Team Base, and the team flag").

**Cross-invariants pinning the border.** The organically reachable set is not a product of
per-coordinate ranges; it is cut out by invariants coupling free coordinates to monotone ones.
`PreSave` enforces the ones whose fields are all modeled (each idempotent on organic saves, so
the identity law survives):

1. roster species ⊆ seen flags (recruit paths call `SetMonSeenFlag`; never recomputed);
2. roster species ⊆ `unk1C` (additively; the game re-derives this one, so it self-heals);
3. each occupied roster slot's friend area is unlocked (recruit-requires-camp, and area
   membership is decided purely by the slot-range partition);
4. recruit events increment `numJoined` (net-positive roster growth, capped 9999);
5. new recruits are placed inside their species' area slot range;
6. mail lists stay compacted toward slot 0 with sentinel-filled tails;
7. checksums and the backup mirror are recomputed from the final state.

The invariants that **cannot** be enforced because one side is passthrough are the actual
danger border, and the reason some flags carry "don't touch" warnings: story/cutscene flags ↔
`SCENARIO_*` script variables ↔ dungeon open lists (all three move together in organic play;
the tool models only the flags), and roster slot numbers ↔ the unmodeled team/leader index
fields (mitigated by never relocating existing slots). Stats/exp/level mutual consistency is a
soft version: freely editable, organically correlated, and the game tolerates mismatches.

**Rules for future features.** (1) Classify a new field before exposing it. (2) Free: expose
set-to-value with the game's own clamp; nothing else needed. (3) Monotone: upward moves are
always safe; a downward move is a rewind and must either drag every coupled field down
consistently or ship with a warning. (4) Derived: never editable; recompute it or leave it to
the game. (5) Passthrough: never write into it; the identity test polices this class
automatically. (6) Modeling a new section is what moves an invariant from the unenforceable
list to the enforceable one; modeling `SCENARIO_*` would make story-flag edits validatable,
and modeling the team/leader indices would let slots be safely relocated.

## Practical takeaway for `RBSave.cs`

Everything `RBSave.cs` currently reads/writes (team name, rescue points, money, stored items,
held items, stored Pokemon roster, `ExclusivePokemonData`, `FriendAreasUnlocked`, and
`MailData` — the mailbox/job/used-Wonder-Mail block) has been positively matched to a specific
decomp section and, for the fields checked, verified against real save bytes. The biggest
**unmodeled** regions if this library's scope grows are: the active 4-member dungeon-ready
party (SavePoke2s), Kecleon shop contents (the tail of the TeamInventory section), the
unidentified 0x594 section, the script-variable region (SCENARIO_MAIN/SUBn and the per-dungeon
open/conquered lists; see RECRUIT_MECHANICS.md's friend-areas-vs-dungeon-unlocks section for
what that means in practice), and the quicksave region entirely.

A minimal cross-platform CLI (`SkyEditor.SaveEditor.Cli`, see `TODO.md`) now exposes
`MarkBossRecruited` and `UnlockFriendArea` (plus money/item edits) as actual runnable commands
on Linux, since the existing WPF UI is Windows-only and untouched by any of this. Building that
CLI also surfaced a real, previously-hidden bug unrelated to the save format itself: a net8.0
console app crashes on `new RBSave(...)` with a `SkyEditor.IO` assembly-version mismatch, caused
by a published-package defect upstream (not anything in this repo's control) — see `TODO.md`
for the root cause and the fix. It was invisible until now because MSTest's test host silently
papers over exactly this class of mismatch.
