"""Imports the art for fishing's extras (night, rain and legendary fish, junk, treasure, rods, the crab cage, pearls,
roe and sushi) into Resources/Textures/_WG/Fishing/loot.rsi. The prototypes in Resources/Prototypes/_WG/Fishing/loot.yml
are written by hand.

Usage: python Tools/_WG/Fishing/port_fishing_loot.py [path/to/Paragon-Moon] [path/to/Meridian-Rift]
"""

import importlib.util
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location("port_fishing", HERE / "port_fishing.py")
pf = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pf)

# state -> (project, sheet, source state)
STATES = {
    "treasure": ("P", "icons/roguetown/misc/structure.dmi", "chest1"),
    "treasure_open": ("P", "icons/roguetown/misc/structure.dmi", "chest1open"),
    "lucky_coin": ("M", "icons/obj/fishing.dmi", "lucky_coin"),
    "algae": ("M", "icons/obj/fishing.dmi", "algae"),
    "rod_fiberglass": ("M", "icons/obj/fishing.dmi", "fishing_rod_telescopic"),
    "rod_iridium": ("M", "icons/obj/fishing.dmi", "fishing_rod_master"),
    "pearl": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "pearl"),
    "cage": ("P", "icons/roguetown/rav/obj/structures/cage.dmi", "cage"),
    "roe": ("M", "icons/obj/food/lizard.dmi", "moonfish_eggs"),
    "caviar": ("M", "icons/obj/food/lizard.dmi", "moonfish_caviar"),
    "sashimi": ("M", "icons/obj/food/meat.dmi", "sashimi"),
    "nigiri": ("M", "icons/obj/food/food.dmi", "nigiri_sushi"),
    "futomaki": ("M", "icons/obj/food/food.dmi", "futomaki_sushi_roll"),
    # Night fish
    "moonfish": ("M", "icons/obj/aquarium/fish.dmi", "dwarf_moonfish"),
    "soulfish": ("M", "icons/obj/aquarium/fish.dmi", "soulfish"),
    "cardinalfish": ("M", "icons/obj/aquarium/fish.dmi", "cardinalfish"),
    "firefish": ("M", "icons/obj/aquarium/fish.dmi", "firefish"),
    # Rain fish
    "sockeye": ("M", "icons/obj/aquarium/fish.dmi", "sockeye"),
    "needlefish": ("M", "icons/obj/aquarium/fish.dmi", "needlefish"),
    "zipzap": ("M", "icons/obj/aquarium/fish.dmi", "zipzap"),
    # Legendary fish
    "legend_frozen": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "crabultra"),
    "legend_cool": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "eelultra"),
    "legend_warm": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "carpultra"),
    "legend_hot": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "anglerultra"),
    "legend_pond": ("M", "icons/obj/aquarium/fish.dmi", "angelfish"),
}


def main():
    cache = {}
    states = {}
    for name, (project, sheet, state) in STATES.items():
        root = pf.PARAGON if project == "P" else pf.MERIDIAN
        if (project, sheet) not in cache:
            cache[(project, sheet)] = pf.frames(root / sheet)
        states[name] = pf.to32(cache[(project, sheet)][state])

    pf.write_rsi(pf.ROOT / "Resources/Textures/_WG/Fishing/loot.rsi", states,
                 "Fish, gear and food from Meridian (icons/obj/fishing.dmi, aquarium/fish.dmi, food/*.dmi); "
                 "pearl, ultra fish, chest and cage from Paragon Moon (modular/Neu_Food/icons/raw/raw_fish.dmi, "
                 "icons/roguetown/misc/structure.dmi, icons/roguetown/rav/obj/structures/cage.dmi); adapted for Wolfgate.")


if __name__ == "__main__":
    main()
