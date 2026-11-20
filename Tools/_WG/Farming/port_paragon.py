"""Ports crops and farm sprites from the owner's Paragon Moon project (a BYOND Roguetown codebase, art CC BY-SA 3.0).

Usage: python Tools/_WG/Farming/port_paragon.py [path/to/Paragon-Moon]

For each crop in CROPS it builds Resources/Textures/_WG/Farming/Crops/<id>.rsi from Paragon's growth stages
(icons/roguetown/misc/crops.dmi, 32x48, trimmed or shrunk to 32x32) and produce icon (icons/roguetown/items/produce.dmi),
then writes the seed, packet and produce prototypes, the wgCrop entries and the Fluent names. It also writes the glass
bottle sprite. Re-run it after changing CROPS; it overwrites what it generated.
"""
import json
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools" / "_WG" / "Wolfmed"))
from dmi_extract import parse, read_description  # noqa: E402

SOURCE = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT.parent / "Paragon-Moon"
TEXTURES = ROOT / "Resources" / "Textures" / "_WG" / "Farming"
PROTOTYPES = ROOT / "Resources" / "Prototypes" / "_WG" / "Farming"
LOCALE = ROOT / "Resources" / "Locale" / "en-US" / "_WG" / "Farming"
COPYRIGHT = "Taken from Paragon Moon (Roguetown lineage), {file}; adapted for Wolfgate."

# TESTING: crop minutes are divided by this for gathering test data; set back to 1 when outposts ship.
GROW_SCALE = 8
SCALE_WORDS = {2: "a half", 4: "a quarter", 8: "an eighth"}

# id: (crop stage prefix, produce state, name, tag, minutes, min C, max C, regrows, yield, flavour)
CROPS = {
    "wgCucumber": ("cucumber", "cucumber", "cucumber", "Vegetable", 60, 12, 34, True, 3, "cucumber"),
    "wgTurnip": ("turnip", "turnip", "turnip", "Vegetable", 45, 0, 24, False, 2, "vegetables"),
    "wgPear": ("pear", "pear", "pear", "Fruit", 150, 2, 28, True, 3, "pear"),
    "wgPlum": ("plum", "plum", "plum", "Fruit", 150, 4, 30, True, 3, "fruity"),
    "wgTangerine": ("tangerine", "tangerine", "tangerine", "Fruit", 150, 14, 36, True, 3, "sour"),
    "wgStrawberry": ("strawberry", "strawberry", "strawberry", "Fruit", 60, 6, 28, True, 3, "sweet"),
    "wgRaspberry": ("raspberry", "raspberry", "raspberry", "Fruit", 75, 4, 28, True, 3, "berry"),
    "wgBlackberry": ("blackberry", "blackberry", "blackberry", "Fruit", 75, 4, 30, True, 3, "berry"),
    "wgSunflower": ("sunflower", "sunflower", "sunflower", "Flower", 90, 10, 36, False, 2, "nutty"),
    "wgRocknut": ("nuts", "rocknut", "rocknut", "Vegetable", 120, 0, 30, True, 3, "nutty"),
    "wgCoffee": ("coffee", "coffee", "coffee cherry", "Fruit", 120, 16, 34, True, 3, "bitter"),
}


def frames(dmi):
    """Every state of a .dmi as its first frame, by name."""
    im, desc = read_description(dmi)
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
    """Fits a 32x48 stage into 32x32: trimmed to its bottom if the plant fits, shrunk to fit otherwise."""
    box = tile.getbbox()
    if box is None:
        return Image.new("RGBA", (32, 32))
    top, bottom = box[1], box[3]
    if bottom - top <= 32:
        start = max(0, bottom - 32)
        return tile.crop((0, start, 32, start + 32))
    plant = tile.crop((0, top, 32, bottom))
    scale = 32 / plant.height
    small = plant.resize((max(1, round(32 * scale)), 32), Image.NEAREST)
    canvas = Image.new("RGBA", (32, 32))
    canvas.paste(small, ((32 - small.width) // 2, 0), small)
    return canvas


def withered(tile):
    """A dead stage: the grown plant drained to brown."""
    out = tile.copy()
    px = out.load()
    for x in range(out.width):
        for y in range(out.height):
            r, g, b, a = px[x, y]
            grey = (r + g + b) // 3
            px[x, y] = (min(255, grey + 30), max(0, grey - 5), max(0, grey - 30), a)
    return out


def packet(icon):
    """The seed packet's picture: the produce, smaller."""
    small = icon.resize((20, 20), Image.NEAREST)
    canvas = Image.new("RGBA", (32, 32))
    canvas.paste(small, (6, 8), small)
    return canvas


def write_rsi(path, states, source):
    path.mkdir(parents=True, exist_ok=True)
    for name, image in states.items():
        image.save(path / f"{name}.png")
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": COPYRIGHT.format(file=source),
        "size": {"x": 32, "y": 32},
        "states": [{"name": name} for name in states],
    }
    (path / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")


def main():
    crops = frames(SOURCE / "icons/roguetown/misc/crops.dmi")
    produce = frames(SOURCE / "icons/roguetown/items/produce.dmi")
    cooking = frames(SOURCE / "icons/roguetown/items/cooking.dmi")

    seeds, packets, foods, entries, names = [], [], [], [], []
    for cid, (prefix, icon, name, tag, minutes, low, high, regrows, crop_yield, flavour) in CROPS.items():
        stages = sorted((s for s in crops if s.startswith(prefix) and s[len(prefix):].isdigit()), key=lambda s: int(s[len(prefix):]))
        if len(stages) < 2 or icon not in produce:
            print(f"skip {cid}: stages {stages}, produce {icon in produce}")
            continue
        # Paragon's last stage is the spent plant and the one before it is ripe; a crop with three stages has no spent one.
        grown = [to32(crops[s]) for s in stages]
        if len(grown) >= 4:
            growing, ripe, dead = grown[:-2], grown[-2], grown[-1]
        else:
            growing, ripe, dead = grown[:-1], grown[-1], withered(grown[-1])
        states = {f"stage-{i + 1}": img for i, img in enumerate(growing)}
        states["harvest"] = ripe
        states["dead"] = dead
        states["produce"] = produce[icon].copy()
        states["seed"] = packet(produce[icon])
        rsi = f"_WG/Farming/Crops/{cid[2:].lower()}.rsi"
        write_rsi(TEXTURES / "Crops" / f"{cid[2:].lower()}.rsi", states, "icons/roguetown/misc/crops.dmi and icons/roguetown/items/produce.dmi")

        key = cid[2:].lower()
        food = f"WGFood{cid[2:]}"
        pack = f"WG{cid[2:]}Seeds"
        stage_count = len(growing)
        seeds.append(f"""- type: seed
  id: {cid}
  name: seeds-wg-{key}-name
  noun: seeds-noun-seeds
  displayName: seeds-wg-{key}-display-name
  plantRsi: {rsi}
  packetPrototype: {pack}
  productPrototypes:
  - {food}
  harvestRepeat: {"Repeat" if regrows else "NoRepeat"}
  lifespan: 30
  maturation: 8
  production: 5
  yield: {crop_yield}
  potency: 10
  growthStages: {stage_count}
  waterConsumption: 0.6
  nutrientConsumption: 0.4
  idealLight: 8
  idealHeat: 298
  splatPrototype: PuddleSplatter
  chemicals:
    Nutriment:
      Min: 1
      Max: 6
      PotencyDivisor: 16
    Vitamin:
      Min: 1
      Max: 3
      PotencyDivisor: 33
""")
        packets.append(f"""- type: entity
  parent: SeedBase
  id: {pack}
  name: packet of {name} seeds
  components:
  - type: Seed
    seedId: {cid}
  - type: Sprite
    sprite: {rsi}
""")
        foods.append(f"""- type: entity
  parent: FoodProduceBase
  id: {food}
  name: {name}
  description: A fresh {name}, grown on an outpost.
  components:
  - type: FlavorProfile
    flavors:
    - {flavour}
  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: 12
        reagents:
        - ReagentId: Nutriment
          Quantity: 6
        - ReagentId: Vitamin
          Quantity: 3
  - type: Sprite
    sprite: {rsi}
  - type: Produce
    seedId: {cid}
  - type: Tag
    tags:
    - {tag}
  - type: StaticPrice
    price: {20 + minutes // 6}
""")
        entries.append(f"""- type: wgCrop
  id: {cid}
  minutes: {minutes / GROW_SCALE:g}
  minTemp: {low}
  maxTemp: {high}
""")
        names.append(f"seeds-wg-{key}-name = {name}\nseeds-wg-{key}-display-name = {name} plant")
        print(f"{cid}: {stage_count} stages")

    bottle = cooking.get("clear_bottle1")
    if bottle is not None:
        write_rsi(TEXTURES / "glass_bottle.rsi", {"icon": bottle}, "icons/roguetown/items/cooking.dmi")

    head = (f"# TESTING: grow times are {SCALE_WORDS[GROW_SCALE]} of their tuned values for gathering test data; "
            f"restore (x{GROW_SCALE}) when outposts ship.\n") if GROW_SCALE != 1 else ""
    head += "# Generated by Tools/_WG/Farming/port_paragon.py from the Paragon Moon project (art CC BY-SA 3.0). Re-run it; don't edit.\n\n"
    (PROTOTYPES / "paragon_crops.yml").write_text(
        head + "\n".join(seeds) + "\n" + "\n".join(packets) + "\n" + "\n".join(foods) + "\n" + "\n".join(entries),
        encoding="utf-8", newline="\n")
    (LOCALE / "paragon_crops.ftl").write_text(
        "# Generated by Tools/_WG/Farming/port_paragon.py.\n\n" + "\n".join(names) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
