"""Ports fish, fishing gear and forage spots from the owner's Paragon Moon and Meridian projects (art CC BY-SA 3.0).

Usage: python Tools/_WG/Fishing/port_fishing.py [path/to/Paragon-Moon] [path/to/Meridian-Rift]

Writes Resources/Textures/_WG/Fishing/*.rsi, Resources/Prototypes/_WG/Fishing/fishing.yml (fish, rod, bait, pond, fish
tables, and the knife and cooking recipes for fish) and Resources/Prototypes/_WG/Foraging/forage.yml (forage spots and
their tables by climate), with Fluent names. Re-run it after changing the tables; it overwrites what it wrote.
"""
import json
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools" / "_WG" / "Wolfmed"))
sys.path.insert(0, str(ROOT / "Tools" / "_WG" / "Farming"))
from dmi_extract import parse, read_description  # noqa: E402

PARAGON = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT.parent / "Paragon-Moon"
MERIDIAN = Path(sys.argv[2]) if len(sys.argv) > 2 else ROOT.parent / "Meridian-Rift"
COPYRIGHT = "Taken from {project}, {file}; adapted for Wolfgate."

# id -> (project, sheet, state, name, price, shellfish)
FISH = {
    "WGFishCarp": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "carpcom", "carp", 40, False),
    "WGFishEel": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "eelcom", "eel", 60, False),
    "WGFishAngler": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "anglercom", "anglerfish", 120, False),
    "WGFishClownfish": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "clownfishcom", "clownfish", 70, False),
    "WGFishSole": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "sole", "sole", 50, False),
    "WGFishPlaice": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "plaice", "plaice", 50, False),
    "WGFishSeabass": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "seabass", "sea bass", 70, False),
    "WGFishCod": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "cod", "cod", 55, False),
    "WGFishSalmon": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "salmon", "salmon", 90, False),
    "WGFishMudskipper": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "mudskipper", "mudskipper", 35, False),
    "WGFishSunfish": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "sunny", "sunfish", 40, False),
    "WGFishShrimp": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "shrimpcom", "shrimp", 45, True),
    "WGFishCrab": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "crabcom", "crab", 80, True),
    "WGFishLobster": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "lobstercom", "lobster", 150, True),
    "WGFishOyster": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "oystercom", "oyster", 70, True),
    "WGFishClam": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "clam", "clam", 40, True),
    "WGFishOctopus": ("P", "modular/Neu_Food/icons/raw/raw_fish.dmi", "octopuscom", "octopus", 110, True),
    "WGFishArcticChar": ("M", "icons/obj/aquarium/fish.dmi", "arctic_char", "arctic char", 80, False),
    "WGFishPerch": ("M", "icons/obj/aquarium/fish.dmi", "perch", "perch", 40, False),
    "WGFishPike": ("M", "icons/obj/aquarium/fish.dmi", "pike_small", "pike", 65, False),
    "WGFishCatfish": ("M", "icons/obj/aquarium/fish.dmi", "catfish", "catfish", 50, False),
    "WGFishSquid": ("M", "icons/obj/aquarium/fish.dmi", "squid", "squid", 75, True),
    "WGFishStingray": ("M", "icons/obj/aquarium/fish.dmi", "stingray", "stingray", 110, False),
    "WGFishSandSurfer": ("M", "icons/obj/aquarium/fish.dmi", "sand_surfer", "sand surfer", 90, False),
    "WGFishGoldfish": ("M", "icons/obj/aquarium/fish.dmi", "goldfish", "goldfish", 150, False),
    "WGFishLanternfish": ("M", "icons/obj/aquarium/fish.dmi", "lanternfish", "lanternfish", 130, False),
    "WGFishPufferfish": ("M", "icons/obj/aquarium/fish.dmi", "pufferfish", "pufferfish", 100, False),
    "WGFishMonkfish": ("M", "icons/obj/aquarium/fish.dmi", "monkfish", "monkfish", 120, False),
    "WGFishSwordfish": ("M", "icons/obj/aquarium/fish.dmi", "swordfish_small", "swordfish", 220, False),
}

# Open water by outside temperature, and ponds: id -> (min C, max C, pond, {fish: weight})
TABLES = {
    "WGFishTable1Frozen": (-100, 0, False, {"WGFishArcticChar": 3, "WGFishCod": 3, "WGFishSalmon": 2, "WGFishPlaice": 2, "WGFishCrab": 1, "WGFishLanternfish": 0.7, "WGFishMonkfish": 0.5}),
    "WGFishTable2Cool": (0, 15, False, {"WGFishCarp": 2, "WGFishPerch": 3, "WGFishPike": 2, "WGFishSalmon": 2, "WGFishCod": 1, "WGFishSole": 1, "WGFishCrab": 1, "WGFishClam": 1, "WGFishEel": 1}),
    "WGFishTable3Warm": (15, 28, False, {"WGFishCarp": 3, "WGFishCatfish": 2, "WGFishPerch": 2, "WGFishEel": 2, "WGFishSeabass": 2, "WGFishShrimp": 1.5, "WGFishSunfish": 2, "WGFishOyster": 1, "WGFishGoldfish": 0.3}),
    "WGFishTable4Hot": (28, 100, False, {"WGFishClownfish": 2, "WGFishAngler": 1, "WGFishLobster": 1, "WGFishOctopus": 1, "WGFishSquid": 1.5, "WGFishStingray": 1, "WGFishMudskipper": 2, "WGFishPufferfish": 1, "WGFishSandSurfer": 1, "WGFishSwordfish": 0.3}),
    "WGFishTablePond": (-100, 100, True, {"WGFishCarp": 3, "WGFishCatfish": 2, "WGFishPerch": 2, "WGFishShrimp": 1, "WGFishGoldfish": 0.5}),
}

# Forage spots: id -> (project, sheet, state, name, description, [(loot, min, max, chance)])
FORAGE = {
    "WGForageBerryBush": ("P", "icons/roguetown/misc/crops.dmi", "berry2", "wild berry bush", "A bush heavy with wild berries.", [("FoodBerries", 2, 4, 1), ("BerrySeeds", 1, 1, 0.2)]),
    "WGForageStrawberries": ("P", "icons/roguetown/misc/crops.dmi", "strawberry2", "wild strawberries", "Small, sweet strawberries growing wild.", [("WGFoodStrawberry", 2, 3, 1), ("WGStrawberrySeeds", 1, 1, 0.3)]),
    "WGForageRaspberries": ("P", "icons/roguetown/misc/crops.dmi", "raspberry2", "wild raspberry canes", "Canes of wild raspberries.", [("WGFoodRaspberry", 2, 3, 1), ("WGRaspberrySeeds", 1, 1, 0.3)]),
    "WGForageBlackberries": ("P", "icons/roguetown/misc/crops.dmi", "blackberry2", "wild brambles", "Brambles thick with blackberries.", [("WGFoodBlackberry", 2, 3, 1), ("WGBlackberrySeeds", 1, 1, 0.3)]),
    "WGForageOnions": ("P", "icons/roguetown/misc/crops.dmi", "onion2", "wild onions", "A clump of wild onions.", [("FoodOnion", 1, 2, 1)]),
    "WGForagePoppies": ("P", "icons/roguetown/misc/crops.dmi", "poppy2", "wild poppies", "Red poppies nodding in the wind.", [("FoodPoppy", 1, 3, 1)]),
    "WGForageSunflowers": ("P", "icons/roguetown/misc/crops.dmi", "sunflower2", "wild sunflowers", "Tall sunflowers turned to the sun.", [("WGFoodSunflower", 1, 2, 1), ("WGSunflowerSeeds", 1, 1, 0.3)]),
    "WGForageTea": ("P", "icons/roguetown/misc/crops.dmi", "tea2", "wild tea shrub", "A shrub of wild tea.", [("LeavesTea", 2, 4, 1)]),
    "WGForageCoffee": ("P", "icons/roguetown/misc/crops.dmi", "coffee2", "wild coffee shrub", "A shrub of ripe coffee cherries.", [("WGFoodCoffee", 2, 3, 1), ("WGCoffeeSeeds", 1, 1, 0.25)]),
    "WGForageNuts": ("P", "icons/roguetown/misc/crops.dmi", "nuts1", "rocknut tree", "A low tree dropping rocknuts.", [("WGFoodRocknut", 2, 4, 1), ("WGRocknutSeeds", 1, 1, 0.2)]),
    "WGForageMushrooms": ("U", "Objects/Specific/Hydroponics/chanterelle.rsi", "harvest", "mushroom ring", "A ring of wild mushrooms in the damp.", [("FoodMushroom", 2, 4, 1)]),
    "WGForageWorms": ("M", "icons/obj/fishing.dmi", "worm", "worm cast", "Turned earth where worms are near the surface. Good bait.", [("WGBaitWorm", 2, 4, 1)]),
}

# Forage tables by outside temperature: id -> (min C, max C, {spot: weight})
FORAGE_TABLES = {
    "WGForageTable1Frozen": (-100, 0, {"WGForageBerryBush": 2, "WGForageMushrooms": 3, "WGForageNuts": 2, "WGForageWorms": 1}),
    "WGForageTable2Cool": (0, 15, {"WGForageBerryBush": 2, "WGForageStrawberries": 2, "WGForageRaspberries": 2, "WGForageMushrooms": 2, "WGForageOnions": 2, "WGForageWorms": 2}),
    "WGForageTable3Warm": (15, 28, {"WGForageBerryBush": 2, "WGForageBlackberries": 2, "WGForageSunflowers": 2, "WGForagePoppies": 1, "WGForageTea": 1, "WGForageOnions": 1, "WGForageWorms": 2}),
    "WGForageTable4Hot": (28, 100, {"WGForageCoffee": 2, "WGForageSunflowers": 2, "WGForagePoppies": 2, "WGForageTea": 1, "WGForageWorms": 1}),
}


def frames(path):
    im, desc = read_description(path)
    width, height, states = parse(desc)
    im = im.convert("RGBA")
    columns = im.width // width
    out = {}
    index = 0
    for state in states:
        x, y = (index % columns) * width, (index // columns) * height
        out.setdefault(state["name"], im.crop((x, y, x + width, y + height)))
        index += state["dirs"] * state["frames"]
    return out


def to32(tile):
    if tile.size == (32, 32):
        return tile
    box = tile.getbbox() or (0, 0, 32, 32)
    top, bottom = box[1], box[3]
    if bottom - top <= 32:
        start = max(0, bottom - 32)
        return tile.crop((0, start, 32, start + 32))
    plant = tile.crop((0, top, tile.width, bottom))
    small = plant.resize((max(1, round(plant.width * 32 / plant.height)), 32), Image.NEAREST)
    canvas = Image.new("RGBA", (32, 32))
    canvas.paste(small, ((32 - small.width) // 2, 0), small)
    return canvas


def write_rsi(path, states, copyright):
    path.mkdir(parents=True, exist_ok=True)
    for name, image in states.items():
        image.save(path / f"{name}.png")
    meta = {"version": 1, "license": "CC-BY-SA-3.0", "copyright": copyright, "size": {"x": 32, "y": 32},
            "states": [{"name": name} for name in states]}
    (path / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")


def main():
    cache = {}

    def get(project, sheet, state):
        root = PARAGON if project == "P" else MERIDIAN
        key = (project, sheet)
        if key not in cache:
            cache[key] = frames(root / sheet)
        return to32(cache[key][state])

    fish_states = {fid.lower(): get(p, sh, st) for fid, (p, sh, st, *_rest) in FISH.items()}
    raw = frames(PARAGON / "modular/Neu_Food/icons/raw/raw_meat.dmi")
    rawfish = frames(PARAGON / "modular/Neu_Food/icons/raw/raw_fish.dmi")
    cooked = frames(PARAGON / "modular/Neu_Food/icons/cooked/cooked_meat.dmi")
    fish_states["fillet"] = raw["fishfillet"]
    fish_states["fillet-cooked"] = cooked["cooked_filet"]
    fish_states["shellfish"] = rawfish["shellfish_meat"]
    fish_states["shellfish-cooked"] = rawfish["shellfish_meat_cooked"]
    gear = frames(MERIDIAN / "icons/obj/fishing.dmi")
    fish_states["rod"] = gear["fishing_rod"]
    fish_states["worm"] = gear["worm"]
    fish_states["doughball"] = gear["doughball"]
    tanks = frames(MERIDIAN / "icons/obj/aquarium/tanks.dmi")
    fish_states["pond"] = to32(next(iter(tanks.values())))
    tex = ROOT / "Resources/Textures/_WG/Fishing"
    write_rsi(tex / "fishing.rsi", fish_states,
              "Fish from Paragon Moon (modular/Neu_Food/icons/raw, cooked) and Meridian (icons/obj/aquarium/fish.dmi); rod, bait and pond from Meridian (icons/obj/fishing.dmi, aquarium/tanks.dmi); adapted for Wolfgate.")

    forage_states = {}
    for sid, (p, sh, st, *_rest) in FORAGE.items():
        if p != "U":
            forage_states[sid.lower()] = get(p, sh, st)
    write_rsi(ROOT / "Resources/Textures/_WG/Foraging/forage.rsi", forage_states,
              "Plants from Paragon Moon (icons/roguetown/misc/crops.dmi); worm cast from Meridian (icons/obj/fishing.dmi); adapted for Wolfgate.")

    y = ["# Generated by Tools/_WG/Fishing/port_fishing.py from Paragon Moon and Meridian (art CC BY-SA 3.0). Re-run it; don't edit.", ""]
    for fid, (p, sh, st, name, price, shell) in FISH.items():
        tag = "WGShellfish" if shell else "WGFish"
        y.append(f"""- type: entity
  parent: FoodBase
  id: {fid}
  name: {name}
  description: A fresh {name}. Chop it on a prep board to {"get its meat" if shell else "fillet it"}.
  components:
  - type: Sprite
    sprite: _WG/Fishing/fishing.rsi
    state: {fid.lower()}
  - type: Item
    size: Small
  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: 10
        reagents:
        - ReagentId: Nutriment
          Quantity: 5
        - ReagentId: Protein
          Quantity: 3
  - type: Tag
    tags: [ {tag}, WGFarmGood ]
  - type: StaticPrice
    price: {price}
""")
    for tid, (low, high, pond, fish) in TABLES.items():
        lines = "".join(f"  - id: {f}\n    weight: {w}\n" for f, w in fish.items())
        y.append(f"- type: wgFishTable\n  id: {tid}\n  minTemp: {low}\n  maxTemp: {high}\n  pond: {str(pond).lower()}\n  fish:\n{lines}")

    y.append("""- type: entity
  parent: FoodBase
  id: WGFoodFishFillet
  name: fish fillet
  description: A raw fillet of fish. Fry it, bake it or stew it.
  components:
  - type: Sprite
    sprite: _WG/Fishing/fishing.rsi
    state: fillet
  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: 6
        reagents:
        - ReagentId: Protein
          Quantity: 4
  - type: Tag
    tags: [ WGChopped ]
  - type: StaticPrice
    price: 25

- type: entity
  parent: FoodBase
  id: WGFoodFishCooked
  name: cooked fish
  description: A flaky, golden fillet.
  components:
  - type: Sprite
    sprite: _WG/Fishing/fishing.rsi
    state: fillet-cooked
  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: 12
        reagents:
        - ReagentId: Nutriment
          Quantity: 6
        - ReagentId: Protein
          Quantity: 4
  - type: Tag
    tags: [ WGFarmGood, WGHomeCooking ]
  - type: StaticPrice
    price: 70
  - type: WGMeal
    traits: [ Fresh, Fortifying ]
    strength: 1.0
    minutes: 12

- type: entity
  parent: FoodBase
  id: WGFoodShellfishMeat
  name: shellfish meat
  description: Raw meat picked from a shell.
  components:
  - type: Sprite
    sprite: _WG/Fishing/fishing.rsi
    state: shellfish
  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: 6
        reagents:
        - ReagentId: Protein
          Quantity: 4
  - type: Tag
    tags: [ WGChopped ]
  - type: StaticPrice
    price: 30

- type: entity
  parent: FoodBase
  id: WGFoodShellfishCooked
  name: cooked shellfish
  description: Tender shellfish, cooked through.
  components:
  - type: Sprite
    sprite: _WG/Fishing/fishing.rsi
    state: shellfish-cooked
  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: 12
        reagents:
        - ReagentId: Nutriment
          Quantity: 6
        - ReagentId: Protein
          Quantity: 4
  - type: Tag
    tags: [ WGFarmGood, WGFineFood ]
  - type: StaticPrice
    price: 90
  - type: WGMeal
    traits: [ Fresh, Cooling ]
    strength: 1.1
    minutes: 12

- type: wgChopRecipe
  id: WGChopFish
  tags: [ WGFish ]
  result: WGFoodFishFillet
  count: 2

- type: wgChopRecipe
  id: WGChopShellfish
  tags: [ WGShellfish ]
  result: WGFoodShellfishMeat
  count: 1

- type: wgChopRecipe
  id: WGChopDoughball
  inputs: [ FoodDoughSlice ]
  result: WGBaitDoughball
  count: 3

- type: wgCookRecipe
  id: WGCookFishFry
  method: Fry
  inputs: [ WGFoodFishFillet ]
  result: WGFoodFishCooked
  seconds: 20

- type: wgCookRecipe
  id: WGCookFishBake
  method: Bake
  inputs: [ WGFoodFishFillet ]
  result: WGFoodFishCooked
  seconds: 30

- type: wgCookRecipe
  id: WGCookShellfishFry
  method: Fry
  inputs: [ WGFoodShellfishMeat ]
  result: WGFoodShellfishCooked
  seconds: 20

- type: entity
  parent: BaseItem
  id: WGFishingRod
  name: fishing rod
  description: Cast it at open water or a pond and wait for a bite. When the line jerks, use the rod to reel in, quickly. Use bait on it to load the hook.
  components:
  - type: Sprite
    sprite: _WG/Fishing/fishing.rsi
    state: rod
  - type: Item
    size: Normal
  - type: WGFishingRod
  - type: StaticPrice
    price: 150
  - type: GuideHelp
    guides:
    - WGFishing

- type: entity
  parent: BaseItem
  id: WGBaitWorm
  name: worm
  description: A wriggling worm, dug from a worm cast. Good bait.
  components:
  - type: Sprite
    sprite: _WG/Fishing/fishing.rsi
    state: worm
  - type: Item
    size: Tiny
  - type: WGBait
    quality: 2
  - type: Stack
    stackType: WGBaitWorm
    count: 1
  - type: StaticPrice
    price: 5

- type: stack
  id: WGBaitWorm
  name: wg-stack-worm
  spawn: WGBaitWorm
  maxCount: 20

- type: entity
  parent: BaseItem
  id: WGBaitDoughball
  name: dough ball
  description: A pinch of dough rolled into bait.
  components:
  - type: Sprite
    sprite: _WG/Fishing/fishing.rsi
    state: doughball
  - type: Item
    size: Tiny
  - type: WGBait
    quality: 1
  - type: StaticPrice
    price: 2

- type: entity
  parent: BaseStructure
  id: WGFishPond
  name: fish pond
  description: A stocked pond for an outpost. Cast a line into it with a fishing rod.
  components:
  - type: Construction
    graph: WGFishPondGraph
    node: built
  - type: Sprite
    sprite: _WG/Fishing/fishing.rsi
    state: pond
  - type: Anchorable
  - type: WGFishingSpot
  - type: StaticPrice
    price: 76
  - type: GuideHelp
    guides:
    - WGFishing

- type: Tag
  id: WGFish

- type: Tag
  id: WGShellfish

""")
    (ROOT / "Resources/Prototypes/_WG/Fishing").mkdir(parents=True, exist_ok=True)
    (ROOT / "Resources/Prototypes/_WG/Fishing/fishing.yml").write_text("\n".join(y), encoding="utf-8", newline="\n")

    f = ["# Generated by Tools/_WG/Fishing/port_fishing.py. Re-run it; don't edit.", ""]
    for sid, (p, sh, st, name, desc, loot) in FORAGE.items():
        sprite = f"    sprite: {sh}\n    state: {st}" if p == "U" else f"    sprite: _WG/Foraging/forage.rsi\n    state: {sid.lower()}"
        lines = "".join(f"    - id: {i}\n      min: {a}\n      max: {b}\n      chance: {c}\n" for i, a, b, c in loot)
        f.append(f"""- type: entity
  id: {sid}
  name: {name}
  description: {desc} Pick it by hand.
  placement:
    mode: SnapgridCenter
  components:
  - type: Transform
    anchored: true
  - type: Clickable
  - type: InteractionOutline
  - type: Sprite
{sprite}
    drawdepth: FloorObjects
  - type: GuideHelp
    guides:
    - WGForaging
  - type: WGForageNode
    loot:
{lines}""")
    for tid, (low, high, spots) in FORAGE_TABLES.items():
        lines = "".join(f"  - id: {s}\n    weight: {w}\n" for s, w in spots.items())
        f.append(f"- type: wgForageTable\n  id: {tid}\n  minTemp: {low}\n  maxTemp: {high}\n  spots:\n{lines}")
    (ROOT / "Resources/Prototypes/_WG/Foraging").mkdir(parents=True, exist_ok=True)
    (ROOT / "Resources/Prototypes/_WG/Foraging/forage.yml").write_text("\n".join(f), encoding="utf-8", newline="\n")
    print(f"{len(FISH)} fish, {len(TABLES)} tables, {len(FORAGE)} forage spots")


if __name__ == "__main__":
    main()
