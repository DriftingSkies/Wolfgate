"""Ports Paragon Moon sprites for homestead machines, goods and kit that had stand-ins, into new RSIs.

Run from the repository root with Paragon Moon (and Meridian-Rift, for a few states) checked out beside it:
    python Tools/_WG/Farming/port_paragon_props.py [path/to/Paragon-Moon]
Each state takes the first direction and frame of its Paragon icon. Roguetown art and the /tg/station art Paragon
carries are both CC BY-SA 3.0; each RSI's copyright says where its states came from.
"""
import json
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Wolfmed"))
from dmi_extract import parse, read_description  # noqa: E402

PARAGON = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[4] / "Paragon-Moon"
MERIDIAN = PARAGON.parent / "Meridian-Rift"
TEXTURES = Path("Resources/Textures/_WG")

# rsi -> (copyright, [(our state, paragon dmi, paragon state)])
RSIS = {
    "Farming/props.rsi": ("Taken from Paragon Moon (Roguetown lineage): "
                          "icons/roguetown/misc/structure.dmi, icons/roguetown/items/misc.dmi, icons/roguetown/items/books.dmi, "
                          "modular/Neu_Food/icons/others/honey.dmi, "
                          "icons/obj/structures/apiary.dmi; and from Meridian (Nova Sector and /tg/station): "
                          "modular_nova/modules/ashwalkers/icons/structures.dmi, "
                          "icons/obj/pipes_n_cables/hydrochem/plumbers.dmi, icons/obj/machines/bitrunning.dmi, "
                          "icons/obj/food/containers.dmi.", [
        ("loom", "icons/roguetown/misc/structure.dmi", "loom"),
        ("millstone", "icons/roguetown/misc/structure.dmi", "millstone"),
        ("scarecrow", "icons/roguetown/misc/structure.dmi", "p_dummy"),
        ("cask", "icons/roguetown/misc/structure.dmi", "barrel1"),
        ("fertilizer_barrel", "icons/roguetown/misc/structure.dmi", "pbarrel1"),
        ("rain_catcher", "icons/roguetown/misc/structure.dmi", "washbin1"),
        ("bee_hive", "icons/obj/structures/apiary.dmi", "beebox-full"),
        ("bee_hive_empty", "icons/obj/structures/apiary.dmi", "beebox-empty"),
        ("packing_crate", "icons/roguetown/misc/structure.dmi", "woodchest"),
        ("fertilizer", "icons/roguetown/items/misc.dmi", "fertilizer"),
        ("honey", "modular/Neu_Food/icons/others/honey.dmi", "greyscale_honey"),
        ("ledger", "icons/roguetown/items/books.dmi", "ledger_0"),
        ("composter", "meridian:modular_nova/modules/ashwalkers/icons/structures.dmi", "wormbarrel"),
        ("digester", "meridian:icons/obj/pipes_n_cables/hydrochem/plumbers.dmi", "fermenter"),
        ("compute_rack", "meridian:icons/obj/machines/bitrunning.dmi", "qserver"),
        ("pickles", "meridian:icons/obj/food/containers.dmi", "pickles"),
        ("preserves", "meridian:icons/obj/food/containers.dmi", "pickles_empty"),
    ]),
    "Farming/kiln.rsi": ("Taken from Paragon Moon (Roguetown lineage), icons/roguetown/misc/forge.dmi.", [
        ("kiln", "icons/roguetown/misc/forge.dmi", "smelter0"),
        ("kiln_lit", "icons/roguetown/misc/forge.dmi", "smelter1"),
    ]),
    "Farming/noticeboard.rsi": ("Taken from Paragon Moon (Roguetown lineage), icons/roguetown/misc/64x64.dmi.", [
        ("noticeboard", "icons/roguetown/misc/64x64.dmi", "noticeboard0"),
    ]),
    "Hunting/props.rsi": ("Taken from Paragon Moon (Roguetown lineage): icons/roguetown/misc/structure.dmi, "
                          "icons/roguetown/topadd/cre/enigma_organs.dmi; smoking rack from Meridian (Nova Sector), "
                          "modular_nova/modules/primitive_cooking_additions/icons/stone_kitchen_machines.dmi.", [
        ("smoking_rack", "meridian:modular_nova/modules/primitive_cooking_additions/icons/stone_kitchen_machines.dmi", "tribal_smoker"),
        ("smoking_rack_working", "meridian:modular_nova/modules/primitive_cooking_additions/icons/stone_kitchen_machines.dmi", "tribal_smoker-drying"),
        ("tanning_rack", "icons/roguetown/misc/structure.dmi", "dryrack"),
        ("heart", "icons/roguetown/topadd/cre/enigma_organs.dmi", "heart"),
        ("liver", "icons/roguetown/topadd/cre/enigma_organs.dmi", "liver"),
    ]),
    "Cooking/props.rsi": ("Taken from Paragon Moon (Roguetown lineage; mayonnaise, ash and potato wedges from /tg/station via "
                          "Paragon): icons/roguetown/misc/gadgets.dmi, icons/roguetown/misc/lighting.dmi, "
                          "modular/Neu_Food/icons/raw/*.dmi, icons/obj/food/containers.dmi, icons/obj/objects.dmi, "
                          "icons/obj/hydroponics/harvest.dmi; cutting board from Meridian (Nova Sector), "
                          "modular_nova/modules/primitive_cooking_additions/icons/cooking_structures.dmi.", [
        ("cutting_board", "meridian:modular_nova/modules/primitive_cooking_additions/icons/cooking_structures.dmi", "cutting_board"),
        ("messkit", "icons/roguetown/misc/gadgets.dmi", "messkit"),
        ("camp_stove", "icons/roguetown/misc/lighting.dmi", "hobostove0"),
        ("camp_stove_lit", "icons/roguetown/misc/lighting.dmi", "hobostove1"),
        ("unbaked", "modular/Neu_Food/icons/raw/raw_dough.dmi", "dough_base"),
        ("mayonnaise", "icons/obj/food/containers.dmi", "mayonnaise"),
        ("burned", "icons/obj/objects.dmi", "ash"),
        ("apple_sliced", "modular/Neu_Food/icons/raw/raw_fruit.dmi", "apple_sliced"),
        ("potato_wedges", "icons/obj/hydroponics/harvest.dmi", "potato_wedges"),
    ]),
}


def first_icon(dmi, state):
    # "meridian:" paths come from the Meridian codebase (its art is CC BY-SA 3.0 too); the rest from Paragon.
    source = MERIDIAN / dmi[len("meridian:"):] if dmi.startswith("meridian:") else PARAGON / dmi
    im, desc = read_description(source)
    width, height, states = parse(desc)
    im = im.convert("RGBA")
    cols = max(1, im.width // width)
    index = 0
    for entry in states:
        if entry["name"] == state:
            x, y = (index % cols) * width, (index // cols) * height
            return im.crop((x, y, x + width, y + height)), (width, height)
        index += entry["dirs"] * entry["frames"]
    raise SystemExit(f"{dmi}: no state {state}")


def main():
    for rsi, (copyright, entries) in RSIS.items():
        out = TEXTURES / rsi
        out.mkdir(parents=True, exist_ok=True)
        size = None
        for ours, dmi, state in entries:
            icon, dims = first_icon(dmi, state)
            assert size is None or size == dims, f"{rsi}: mixed sizes"
            size = dims
            icon.save(out / f"{ours}.png")
        meta = {"version": 1, "license": "CC-BY-SA-3.0", "copyright": copyright,
                "size": {"x": size[0], "y": size[1]}, "states": [{"name": e[0]} for e in entries]}
        (out / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8")
        print(rsi, len(entries))


if __name__ == "__main__":
    main()
