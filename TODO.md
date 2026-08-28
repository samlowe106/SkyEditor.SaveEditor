# TODO

- See `SAVE_FORMAT.md` for the full save-file map (128KB physical layout,
  primary/backup/quicksave regions, and every section inside the player
  data blob) built this session by cross-referencing the decomp against
  real save bytes. Its "Write-path bugs found and fixed this session"
  section documents four real, previously-undetected bugs (see below for
  a summary) — read it before touching `RBSave.cs`'s save path again.
  It also lists everything that isn't modeled in `RBSave.cs` yet, in more
  detail than this file: the active 4-member dungeon-ready party
  (`SavePoke2s`), Kecleon shop contents, the script-variable region, and
  one still-unidentified 1428-byte section (`sub_8095624` in the decomp).
  The Wonder Mail/job board (`MailInfo`) block is now fully modeled
  (`RBMailData`/`RBWonderMail`, GUI "Wonder Mail" tab, password
  encode/decode ported from the samlowedotdev wondermail JS library).
- **Save-integrity session** (2026-08-27, after the flag-mystery fix):
  audited every roster-derived field against the decomp (full sync matrix
  now in `SAVE_FORMAT.md`), which surfaced and fixed three more real
  bugs — the backup-block fallback read garbage via mixed byte/bit
  offsets, held-item loops wrote 50 slots into a 20-slot bag and wiped
  the first ~68 storage stacks on every save, and the storage-quantity
  cap of 1024 overflowed the 10-bit field to 0 (SAVE_FORMAT.md bugs
  8-10). `PreSave` now also clamps money/IQ/level/HP to the game's own
  maxima and enforces the roster-implies-seen invariant on monster seen
  flags. Same session: story-flag listing (`RBStoryFlags`, CLI + GUI),
  the Wonder Mail tab with safe removal (game-identical reset+compact
  semantics), and a BitBlock performance pass (save load ~7ms -> ~4ms,
  encode ~9ms -> ~3ms). Also added Toolbox (the 20-slot carried bag) and
  per-Pokemon held-item viewing/editing: `RBStoredPokemon.HeldItemId`/
  `HeldItemQuantity` (decomposed out of `Unk2` per WritePoke1Bits),
  Toolbox section on the General tab, Held Item row in the roster editor,
  both in `ske info`, with clamps (item id 0-239, stack 0-127) folded
  into `ClampToGameLimits`. The three item stores (Toolbox, storage,
  held) are independent in the engine; no cross-syncing is needed.
- **No-op/fingerprint sweep** (2026-08-27, closing pass): loading the real
  mGBA save and resaving it untouched was NOT byte-identical, which
  exposed two over-normalization bugs (SAVE_FORMAT.md bugs 11-12): the
  clamp pass zeroed stale held-item quantity bits behind an empty item id,
  and name re-encoding zero-filled the 10-byte buffer past the terminator,
  both erasing organic garbage the game preserves. Fixed (quantity rides
  along verbatim; `RBStoredPokemon` keeps the raw 80-bit name buffer and
  only regenerates it on an actual rename). `RBSaveAlgebraTests` now pins
  the pipeline's algebraic laws byte-for-byte against the real save:
  identity (no-op resave reproduces all 131072 bytes), idempotence,
  invertibility (edit-save-revert-save is exact for money/flags/friend
  areas/held items/Toolbox), commutativity for disjoint edits, and the
  ratchet characterization (roster add+save+remove+save differs from the
  original in exactly numJoined + the species' seen and ever-recruited
  bits + checksums + backup mirror). See SAVE_FORMAT.md's "Byte-level
  algebra of the save pipeline" section for the full statement, including
  the follow-up classification of the whole modeled state into free
  (invertible group action), monotone (ratchets), derived (checksums,
  backup, game-recomputed log fields), and passthrough regions, plus the
  cross-invariants that couple them. That pass also added one more
  invariant to PreSave: occupied roster slot implies that slot's friend
  area is unlocked (recruit-requires-camp; slot-based via
  `RBFriendAreaCapacity.AreaForSlot`), closing the last roster-derived
  gap for direct API adds that bypass `RecruitFromGuide`.
- **GUI bug round + learnsets + inventory tabs** (2026-08-27, after Sam's
  first hands-on session): (1) recruiting no longer yanks the Friend
  Areas list's scroll position — the grouped/flat/nested ListBoxes now
  set `AutoScrollToSelectedItem="False"`, since programmatic
  `SelectedFriendArea` writes were bring-into-viewing the tall expanded
  area item. (2) Tool-added recruits now get real movesets:
  `tools/build_learnsets.py` decodes every species' level-up learnset
  from the ROM's `wazapara` file (SIRO → MoveDataFile → per-species
  varint streams, the exact data `GetLevelUpMoves` reads;
  `RBLearnsetData.generated.cs`), and `RBLearnsets.WildMoveset` mirrors
  the spawn rule (`sub_8072AC8`: fill four slots in learnset order, then
  each later move at ≤ level overwrites a random slot) with the
  deterministic last-four pick, one of the reachable outcomes. Verified
  against the organic save: fresh recruits (Aron/Doduo/Magnemite) match
  exactly; Poochyena Lv.20 and the two Lv.28 Duskull are each exact
  random-overwrite outcomes, confirming the model. Applied in
  `ToStoredPokemon` (recruits + previews) and `MarkBossRecruited`; move
  flags match organic recruits (valid + AI-usable). `RBLearnsetTests`
  pins all of this (148 tests total now). (3) Staged-but-unsaved roster
  additions are always deletable, even story bosses (the birds had no
  Farewell option, so in-flight adds couldn't be undone): `CanDelete`
  now ORs in `IsPending`, and deleting a staged add also reverts exactly
  the side effects that add performed (its cutscene flag via
  `CutsceneFlagFromAdd`, its area auto-unlock via `AreaUnlockedByAdd`),
  with a softer confirm dialog. Saved bosses stay locked as before.
  (4) Inventory redesign: the General tab's right column is now a
  three-tab control (Storage / Toolbox / Held by Pokemon); Add targets
  the open tab, storage rows have -1/x removal, the held tab lists every
  roster member with its held item (Take → storage), and item rows drag
  and drop between all three containers and from the all-items list
  (hovering a tab header mid-drag switches tabs; displaced held items go
  to storage; thrown stacks keep their count, gear moves at stack 0).
- **Hero/partner/leader/base/flag research** (2026-08-27, documented for
  future editability — full writeup in `SAVE_FORMAT.md`'s "Hero, partner,
  team leader, Team Base, and the team flag" section and the
  RecruitedPokemon tail table): hero and partner are just roster entries
  with sentinel MetAt values (64/65), already editable today; the current
  team leader and on-team membership are 16-bit roster slot indices at
  the tail of the RecruitedPokemon block (byte-verified layout,
  134,807 bits total), currently passthrough — model these to make
  leader/party editable and to allow safe slot relocation; the Team Base
  is BASE_KIND (= the existing `RBSave.BaseType`, decomp-confirmed) x
  BASE_LEVEL, and the team flag is FLAG_KIND, all plain bytes in the
  global script-variable image at file 0x004 (key entries now mapped and
  verified in SAVE_FORMAT.md's header section) — BASE_LEVEL and
  FLAG_KIND would be one-liners next to BaseType. Also identified the
  0x594 section's shape (32 records + one full Pokemon + 33 u32s,
  plausibly friend-rescue state) and corrected the header doc: the
  per-section header fields are end-of-stream bit phases (total bits mod
  8), not byte lengths.
- **Level/Exp/stat coupling + rank coupling** (2026-08-27): the full
  per-species growth tables (cumulative Exp and exact per-level
  HP/Atk/SpAtk/Def/SpDef gains, plus level-1 base stats from
  monster_data.json) are now bundled in the library as
  `Resources/RBGrowthTables.bin` (`tools/build_growth_tables.py`,
  `RBGrowthTables.cs`), superseding the GUI-only ExpCurves.bin.
  `RBGrowthTables.SetLevel` mirrors LevelUp/level-down in
  src/dungeon_leveling.c exactly (add/subtract each level's row, caps
  999/255, floors 1, Exp snaps to the level's requirement); growth is a
  fixed table with no RNG, so this yields exactly legitimate stats.
  Verified: base + summed gains reproduce all 204 recruit-guide entries
  (`RBGrowthTablesTests`, 153 tests total). GUI: editing Level updates
  stats and Exp; editing Exp derives the level (game's threshold rule)
  and updates stats while keeping the typed Exp. Rescue Team rank got
  the same treatment: a rank ComboBox on the General tab sets points to
  the rank's minimum (`RBRescueTeamRanks.MinPointsFor`), and rank always
  follows points.
- **Git hooks** (2026-08-27): versioned in `.githooks/` (see its README;
  enable per clone with `git config core.hooksPath .githooks`).
  pre-commit runs `dotnet format whitespace --verify-no-changes` on the
  staged .cs files only (so untouched legacy files never block), builds
  the three net8.0 projects, and py_compile/ruff checks staged .py
  files; pre-push runs the full test suite. `.editorconfig` holds the
  whitespace rules plus suggestion-level C# style preferences (no
  charset or line-ending pin: the inherited files carry BOMs and mixed
  endings, and normalizing them is pure churn). A one-time whitespace
  normalization of the 15 legacy files that failed the check went in
  with this.
- **Evolution history decoded** (2026-08-27): `RBStoredPokemon.Unk1` is
  now fully understood: floor(7) + first-evolution level(7) +
  second-evolution level(7) (decomp `unkC[0..1]`, written by the
  evolution routine, read only by Gulpin's move-remembering list, which
  is pure learnset-table math -- see SAVE_FORMAT.md "Evolution
  history"). Exposed as `FirstEvolutionLevel`/`SecondEvolutionLevel`,
  clamped 0-100, editable in the roster pane ("Evolved at"), verified
  0/0 on every organic roster member and round-tripped bit-exactly
  (`RBEvolutionHistoryTests`). Wild-recruited evolved forms legitimately
  carry 0/0, so tool-added evolved recruits need no history.
- ~~Test against real save files.~~ Resolved: `RRT.sav`, a real 128KB mGBA
  `.srm` from Sam's own legally-dumped cartridge (ROM SHA-1 verified
  against the decomp's `red.sha1`), is now an embedded test fixture
  (`RRTSaveDataTests.cs`). Loading it through mGBA-qt on this machine and
  cross-checking in-game values against decoded save bytes is what
  surfaced the bugs below — round-tripping against a real, non-trivial
  save is a much stronger check than the synthetic/small `BRT.sav` ever
  was. Explorers of Sky/Time saves remain untested against real data if
  that game family gets picked up here.
- **Four real bugs found and fixed this session** (full writeup in
  `SAVE_FORMAT.md`), found only by decoding *specific known values* from
  round-tripped real save data, not just checking the checksum stayed
  valid:
  1. Held-item slot stride was 33 bits, should be 23 (`RBHeldItem`).
  2. `StoredPokemon` loading stopped at the first empty roster slot,
     silently returning an empty roster on any save whose occupied slots
     aren't packed from index 0 (real saves aren't — a real save had 20
     Pokemon scattered across slots 54-398 with gaps throughout). Fixed
     by scanning all 413 slots; saving now preserves each Pokemon's
     original slot via `RBStoredPokemon.SlotIndex` instead of compacting
     the list, since untouched `team[]`/leader index fields elsewhere in
     the same section reference specific slot numbers (see next item).
  3. **Most severe**: the primary→backup save copy in `RBSave.PreSave()`
     mixed up byte and bit units, corrupting roughly bytes `[3072, 24576)`
     of the primary save — most of the recruited-Pokemon roster — on
     *every single save* this library ever wrote. Checksums stayed valid
     throughout, because they're computed from whatever data is present,
     not checked against an independent source of truth.
  4. `RBSave.ToByteArray()` ran the entire save-encoding pipeline twice
     per call (redundant, not harmful once bug 3 is fixed). The same
     pattern exists in `TDSave.cs`/`SkySave.cs`, not touched.
- Quicksave region (`0x10000`-`0x20000` in the save file, written by
  `dungeon_serializer.c`) is completely unexplored beyond what came up
  incidentally earlier this session (the per-entity `bossFlag` scratch
  field). Not needed for the boss-recruited feature, but would matter for
  any mid-dungeon save editing.
- `StoredItems`/`HeldMoney`/`StoredMoney` still haven't had the same
  real-data-decode verification `HeldItems`, `ExclusivePokemonData`, and
  `StoredPokemon` got this session — their offsets are close to where
  decomp arithmetic says they should be (within one item-slot width) but
  weren't independently checked against known-good values from the real
  save. Given bug 3 above was hiding in exactly this kind of
  "checksum-valid but never actually verified" gap, worth doing before
  trusting them fully.
- ~~Friend Area import from CSV (or similar)...~~ Superseded by a better version of
  the same idea: `RBRecruitGuide` (`SkyEditor.SaveEditor/MysteryDungeon/Rescue/`)
  gives, for every recruitable species, the *exact* stats a legitimate recruit would
  have at its easiest real recruit spot — real HP/Atk/SpAtk/Def/SpDef/Exp, not an
  approximation, extracted from the ROM's own per-species growth table (see
  `tools/build_recruit_guide.py`'s module docstring for how — short version: the
  growth table format and its AT-decompression algorithm are fully decompiled, but
  the actual per-species compressed data isn't reproduced anywhere in the decomp
  checkout, so the script pulls it directly from a real ROM dump using byte offsets
  that are, unusually, sitting in `data/system_sbin.s` in the decomp as plain text).
  `RBSave.RecruitFromGuide(RecruitGuideEntry)` adds the Pokemon (species, level,
  stats, and "met at" dungeon+floor all matching a real recruit) to the roster; the
  GUI exposes this as a "+ Recruit Selected" picker on the Friend Areas tab, scoped
  to whichever species can actually live in the selected area. Now also sets a real
  moveset (`RBLearnsets.ApplyWildMoveset` — see the 2026-08-27 GUI bug round entry
  above); edit Attack1-4 afterward if a different legitimate outcome is wanted.
  Regenerate `RBRecruitGuideData.generated.cs` by re-running the script if
  `guide.md` ever changes, and `RBLearnsetData.generated.cs` via
  `tools/build_learnsets.py`.
  - **Fixed this pass** (see `SAVE_FORMAT.md`'s "Write-path bugs" 5-7 for the full
    writeup): `RecruitFromGuide`/`MarkBossRecruited` now place the new Pokemon in a
    free roster slot within the target friend area's *own slot range*
    (`RBFriendAreaCapacity` — friend-area membership turned out to be determined by
    roster slot index, not species, so just appending anywhere silently produced a
    Pokemon that didn't actually show up living in the intended area in-game) and
    auto-unlock that area. That pass also changed `AdventureBitsBitLength` from 1463
    to 1449, believing 1463 a mismeasurement; this was WRONG (the tally missed the
    14-bit `WriteDungeonLocationBits` tail) and misaligned every `ExclusivePokemonData`
    read/write by 14 bits, manufacturing the "impossible cutscene flag" mystery. Now
    reverted to 1463; see `FLAG_MYSTERY_INVESTIGATION.md` and `SAVE_FORMAT.md` bug 5.
  - The Adventure Log's recruited-count/species-flag (`RBSave.NumPokemonRecruited`/
    `SetRecruitedSpeciesFlag`) are **not** bumped inline by `RecruitFromGuide`/
    `MarkBossRecruited` — they're computed once in `PreSave()` as a net diff between
    `StoredPokemon` as it is at save time and as it was when the file was loaded (or
    since the last save), via `RBSave.UpdateAdventureLogForRosterChanges`. So adding two
    Pokemon and removing one again before saving only counts as +1, and a species that
    got added then removed before saving never gets flagged as recruited at all.
  - **Pokemon added via `RecruitFromGuide` before this fix landed are sitting in
    whatever slot happened to be lowest-free at the time**, not necessarily within their
    species' real friend area's range — they won't show up living in the right (or maybe
    any) area in-game. There's no reliable way to auto-detect and relocate them after the
    fact (no marker distinguishes a tool-added entry from a legitimate low-level recruit);
    remove and re-add them through the fixed code instead.
- The Friend Areas tab's right-hand panel now shows three stacked sections for
  whichever area is selected on the left: a graphic placeholder, "who lives here"
  (recruited roster Pokemon whose species' static home area — `RBRecruitGuide.HomeAreaOf`
  — matches), and the existing "who can be added" picker; the whole stack scrolls as
  one `ScrollViewer` rather than each list scrolling independently. No friend-area
  artwork is bundled yet — `LoadAreaGraphic()` in `MainWindow.axaml.cs` looks for an
  optional `avares://SkyEditor.SaveEditor.Gui/Assets/FriendAreas/{enum name}.png` and
  falls back to a text placeholder, so dropping in 58 real PNGs later (per the
  earlier "Full area artwork" scoping decision) is all that's needed — no further
  code changes required. Fetching real wiki artwork for all 58 areas is still
  outstanding.
- `RBStoredPokemon.Floor` (new this pass) is the first real fix to `MetAt`'s known
  gap: the decomp's `DungeonLocation` struct is `{ id (7 bits), floor (7 bits) }`,
  but `MetAt` only ever exposed the 7-bit `id` half. `Floor` exposes the other half
  (the first 7 bits of `Unk1`, immediately after `MetAt` in the bitstream). The rest
  of `Unk1` (14 bits) and all of `Unk2` (43 bits) are still unmodeled.
- Avalonia migration for `SkyEditor.SaveEditor.UI.WPF` — deferred; the WPF
  UI project (Xceed AvalonDock/DataGrid/Toolkit) stays Windows-only for now.
  Core library + tests build cross-platform (net8.0) as of this pass.
- ~~A cross-platform CLI/front-end was considered...~~ Resolved: see
  `SkyEditor.SaveEditor.Cli` above. The WPF UI itself is unmodified by any
  of this session's work (still Windows-only, still has no knowledge of
  `MarkBossRecruited`/`FriendAreasUnlocked`/`ExclusivePokemonData`).
- **`SkyEditor.SaveEditor.Gui`**: a small cross-platform Avalonia (11.3.20)
  desktop app, same scope as the CLI (open a save, view roster/bosses/friend
  areas, mark a boss recruited, unlock a friend area, add money, add an
  item, save) but with an actual window instead of flags. Deliberately not
  a port of the full WPF editor (that's a much bigger effort -- multiple
  game families, AvalonDock docking layout, DataGrid/Toolkit -- and was
  explicitly out of scope for this pass); this only covers what the CLI
  already covers. Code-behind, not full MVVM, to keep it small. Needs its
  own copy of the `AssemblyVersionUnification` module-initializer fix (see
  the CLI bug writeup below) since it's a separate entry assembly.
- Nullable reference types are now enabled (`<Nullable>enable</Nullable>`)
  on `SkyEditor.SaveEditor` and its test project. This surfaced ~362
  pre-existing warnings (mostly constructors not initializing non-nullable
  properties) across the existing codebase that were never addressed —
  worth cleaning up incrementally, but out of scope for the pass that
  enabled the setting.
- ~~The exact save-file bit offset of `ExclusivePokemonData`...~~ Resolved:
  `RBOffsets.AdventureDataOffset` (0x4EE0 bytes) is now wired into
  `RBSave.Init()`/`PreSave()`. Derived by walking `save.c:WriteSavetoPak`'s
  section order (`SaveRecruitedPokemon` → `SavePoke2s` → `SaveTeamInventory`
  → `SaveRescueTeamInfo` → `SaveFriendAreas` → `SaveAdventureData`) anchored
  to the already-verified `TeamNameStart` offset, then empirically confirmed
  against `BRT.sav`: PlayTime/GameOptions/AdventureBits all decode to
  plausible values (small hours/minutes/seconds, small counters) at that
  offset rather than noise, and the total bit width (2041 bits) fits the
  reserved 256-byte buffer with only 7 bits to spare. See the remarks on
  `RBOffsets.AdventureDataOffset` in `RBSave.cs`.
- ~~Only a handful of `CutsceneFlagID` values are exposed...~~ Resolved: all
  35 named flags from `include/constants/cutscenes.h` are now in
  `RBCutsceneFlag` (index-for-index verified against the decomp enum, no
  gaps). See `RECRUIT_MECHANICS.md` for the full writeup of what actually
  gates each of the 19 story bosses' first-encounter recruitability (two
  independent mechanisms, most flags are cosmetic-only, the three Regis
  are item-possession-gated rather than flag-gated) — read it before
  touching `RBBossEncounters`/`RBSave.CanCurrentlyRecruit` again, a first
  pass at the Regi mechanic got it wrong in a way that would have quietly
  broken recruit-gating for them.
- ~~Friend area "bought/unlocked" state...~~ Resolved: `RBOffsets.FriendAreaOffset`
  (0x4ED8 bytes, 58 bits -- `FRIEND_AREA_COUNT`) is wired into
  `RBSave.FriendAreasUnlocked`/`UnlockFriendArea()`. The offset is bracketed
  by two independently-verified real offsets (`TeamNameStart` before it,
  `AdventureDataOffset` right after it), and `0x4ED8 + 8 == 0x4EE0` checks
  out exactly against decomp arithmetic; a real-save test also confirms at
  least one area is unlocked and index 0 (`RBFriendArea.None`) never is.
  Separate from which friend area a recruited Pokemon's species belongs to
  (that's static per-species data, not save data; see `monster_data.json`'s
  `friendArea` field in the decomp, already used by `guide.md`) -- the CLI's
  `unlock-friend-area` command is a separate action from `mark-boss`, since
  the user may want to unlock an area without recruiting anything into it
  yet, or vice versa.
- A cross-platform CLI now exists: `SkyEditor.SaveEditor.Cli` (`ske`), a
  minimal net8.0 console app with `info`, `mark-boss`, `unlock-friend-area`,
  `add-money`, and `add-item` subcommands. `mark-boss` fills in placeholder
  stats (HP/Attack/etc. scaled off `--level`, default 30) since the decomp's
  per-boss battle-setup levels (`gZapdosConfigLevel` and friends) are a
  temporary in-battle level-up amount, not each boss's canonical level --
  edit the added Pokemon's stats afterward if exact values matter. Writes
  back to the input file by default, after copying the pre-edit bytes to
  `<file>.bak`; `--out` writes elsewhere and leaves the input alone.
- **Found and fixed a real cross-platform-blocking bug while wiring up the
  CLI**: constructing an `RBSave` (or any other `*Save` type) from a plain
  net8.0 console app crashed immediately with
  `FileNotFoundException: SkyEditor.IO, Version=5.0.8.0` -- before any of
  the app's own code ran. Root cause: the published `SkyEditor.Core` 4.2.10
  NuGet package's compiled metadata references `SkyEditor.IO,
  Version=5.0.8.0`, but the `SkyEditor.IO` 5.0.8 package actually on
  nuget.org ships a DLL whose real `AssemblyVersion` is `5.0.0.0` -- the
  package version and the assembly version diverged upstream, in packages
  we don't control. On .NET Framework this class of mismatch is normally
  papered over by an `app.config` `bindingRedirect` (see
  `SkyEditor.SaveEditor.UI.WPF/App.config` for examples of the pattern
  applied to other dependencies), but .NET Core/.NET 5+ has no equivalent.
  The 73-test suite never caught this because MSTest's test host installs
  its own lenient assembly-resolution fallback that silently papers over
  exactly this kind of mismatch. Fixed with a `[ModuleInitializer]` in the
  CLI's own entry assembly (`SkyEditor.SaveEditor.Cli/AssemblyVersionUnification.cs`)
  that registers an `AssemblyLoadContext.Default.Resolving` handler falling
  back to loading by simple name. This has to live in the outermost
  (application) assembly, not the `SkyEditor.SaveEditor` library itself --
  the failure happens while CoreCLR is still binding `SkyEditor.SaveEditor`'s
  own reference to `SkyEditor.Core` (and from there to `SkyEditor.IO`),
  which happens before that library's own module initializer, or any of its
  managed code, gets a chance to run. **Any future net8.0 consumer of this
  library (e.g. an Avalonia GUI) will need the same fix.**
