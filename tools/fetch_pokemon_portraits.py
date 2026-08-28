"""Downloads a "normal" dialogue portrait per Rescue Team species into
SkyEditor.SaveEditor.Gui/Assets/PokemonPortraits/{species id}.png.

Source: mysterydungeonwiki.com's "Explorers TD Pokemon Portraits" category (CC BY-NC-SA
4.0, per the site's footer -- same license basis already relied on for the bundled item
icons; see ItemIcons.generated.cs). Explorers of Time/Darkness's portrait style is used
because Rescue Team (GBA) never had a portrait-per-emotion asset set of its own to draw
from; this is the closest official art for each species. (mysterydungeonwiki.com also has
a "Rescue Team DX" portrait set, from the actual Switch remake of this game -- arguably a
closer stylistic match than Explorers TD -- not used here only because Explorers TD is
what was asked for; swapping WIKI_GAME_PREFIX below to "Rescue Team DX" is a one-line
change if that's preferred later.)

Species names/IDs come straight from Resources/en/RBPokemon.txt (the same list
Lists.RBPokemon loads at runtime), so this stays in sync with that file automatically.
IDs 0 ("??????????"), 421 ("Decoy"), and 422 ("Statue") aren't real species and are
skipped.

Several groups of IDs share one plain species name in RBPokemon.txt but have visually
distinct per-ID portraits on the wiki, so they're special-cased instead of using the
generic "{name} portrait normal" filename:
  - Unown, IDs 201-226: the wiki has one file per letter ("Unown A" .. "Unown Z"); IDs
    are assumed to run in alphabetical order (201=A .. 226=Z), matching the count (26).
  - Unown "!", ID 415: a 27th Unown form the wiki files as "Unown !". ID 416 is also
    "Unown" in RBPokemon.txt (presumably the "?" form) but the wiki has no matching
    file for it as of this writing -- left unmapped, so it just gets the placeholder.
  - Deoxys, IDs 414/417/418/419: the wiki has one file per forme -- plain "Deoxys" for
    414 (its default/Normal forme), "(Attack Forme)"/"(Defense Forme)"/"(Speed Forme)"
    for the other three. The forme order for 417/418/419 is assumed (Attack, Defense,
    Speed) since that's the standard ordering everywhere else in this repo's data
    (RBRecruitGuideData, monster_data.json), not independently verified against
    in-game sprites -- the three only differ by recolor.

A handful of other species names may not match the wiki's file-naming convention
exactly (punctuation, hyphenation); NAME_OVERRIDES below maps RBPokemon.txt's name to
the wiki's, for any that turn out to differ. Uses MediaWiki's Special:FilePath redirect
so this doesn't need to know each file's upload-hash path ahead of time.

Rerun any time RBPokemon.txt changes or to pick up missing species; already-downloaded
files are skipped unless --force is passed.
"""

import argparse
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SPECIES_LIST = REPO_ROOT / "SkyEditor.SaveEditor" / "Resources" / "en" / "RBPokemon.txt"
OUTPUT_DIR = REPO_ROOT / "SkyEditor.SaveEditor.Gui" / "Assets" / "PokemonPortraits"

WIKI_FILE_PATH_BASE = "https://mysterydungeonwiki.com/wiki/Special:FilePath/"
WIKI_GAME_PREFIX = "Explorers TD"

# id 0 is the "no species" placeholder; 421/422 are in-game decoy/statue entities, not
# real Pokemon -- none of these have (or need) a portrait.
SKIP_IDS = {0, 421, 422}

UNOWN_IDS = list(range(201, 227))  # A-Z, alphabetical
UNOWN_EXTRA_NAMES = {415: "Unown !"}  # 416 ("?" form) has no matching file on the wiki
DEOXYS_FORME_NAMES = {
    414: "Deoxys",  # default/Normal forme has no "(... Forme)" suffix on the wiki
    417: "Deoxys (Attack Forme)",
    418: "Deoxys (Defense Forme)",
    419: "Deoxys (Speed Forme)",
}

# RBPokemon.txt's name -> the wiki's file-naming spelling, only where they differ.
NAME_OVERRIDES: dict[str, str] = {
    "Nidoran♀": "Nidoran F",
    "Nidoran♂": "Nidoran M",
}

REQUEST_DELAY_SECONDS = 0.4


def load_species() -> dict[int, str]:
    species: dict[int, str] = {}
    for line in SPECIES_LIST.read_text(encoding="utf-8-sig").splitlines():
        line = line.strip()
        if not line:
            continue
        id_str, _, name = line.partition("=")
        species[int(id_str)] = name
    return species


def wiki_name_for(species_id: int, species_name: str) -> str:
    if species_id in UNOWN_IDS:
        letter = chr(ord("A") + (species_id - UNOWN_IDS[0]))
        return f"Unown {letter}"
    if species_id in UNOWN_EXTRA_NAMES:
        return UNOWN_EXTRA_NAMES[species_id]
    if species_id in DEOXYS_FORME_NAMES:
        return DEOXYS_FORME_NAMES[species_id]
    return NAME_OVERRIDES.get(species_name, species_name)


def download(wiki_name: str) -> bytes:
    filename = f"{WIKI_GAME_PREFIX} - {wiki_name} portrait normal.png"
    url = WIKI_FILE_PATH_BASE + urllib.parse.quote(filename)
    request = urllib.request.Request(url, headers={"User-Agent": "Sky-Editor-portrait-fetch/1.0"})
    with urllib.request.urlopen(request) as response:
        return response.read()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--force", action="store_true", help="re-download files that already exist")
    args = parser.parse_args()

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    species_by_id = load_species()

    succeeded: list[str] = []
    failed: list[str] = []
    skipped_existing = 0

    for species_id, name in sorted(species_by_id.items()):
        if species_id in SKIP_IDS:
            continue

        out_path = OUTPUT_DIR / f"{species_id}.png"
        if not args.force and out_path.exists():
            skipped_existing += 1
            continue

        wiki_name = wiki_name_for(species_id, name)
        try:
            data = download(wiki_name)
        except urllib.error.HTTPError as ex:
            failed.append(f"#{species_id} {name} (as '{wiki_name}'): HTTP {ex.code}")
            continue
        except urllib.error.URLError as ex:
            failed.append(f"#{species_id} {name} (as '{wiki_name}'): {ex.reason}")
            continue
        finally:
            time.sleep(REQUEST_DELAY_SECONDS)

        out_path.write_bytes(data)
        succeeded.append(f"#{species_id} {name}")

    print(f"Downloaded {len(succeeded)} portrait(s); "
          f"{skipped_existing} already had a file; {len(failed)} failed.")
    if failed:
        print("\nFailed (check NAME_OVERRIDES or the wiki page for these):")
        for line in failed:
            print(f"  {line}")
        sys.exit(1)


if __name__ == "__main__":
    main()
