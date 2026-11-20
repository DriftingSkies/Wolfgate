"""Ports hunting from the owner's Paragon Moon project (art CC BY-SA 3.0): game animals spread over the planets by
climate, what they butcher into, and the racks that preserve it.

Usage: python Tools/_WG/Hunting/port_hunting.py [path/to/Paragon-Moon]

Writes Resources/Textures/_WG/Hunting/*.rsi, Resources/Prototypes/_WG/Hunting/hunting.yml (game, meat, pelts, leather,
bones, jerky, the racks, and their recipes), and adds the game to the planets' wildlife tables (PLACES) between
"WOLFGATE-HUNTING" comments, which a re-run replaces. Re-run it after changing the tables.
"""
import json
import re
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools" / "_WG" / "Wolfmed"))
from dmi_extract import parse, read_description  # noqa: E402

SOURCE = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT.parent / "Paragon-Moon"
MOBS = "icons/roguetown/mob/monster"
COPYRIGHT = "Taken from Paragon Moon (Roguetown lineage), {file}; adapted for Wolfgate."

# Game: id -> (sheet, alive, dead, name, description, kind, health, speed, damage, meat, pelt, prized cut, fatty)
# kind: prey (bolts from anyone close) or predator (hunts whoever it sees). The bigger the animal (its health), the more
# meat and bones it gives: about a cut for every 22 health and a bone for every 55.
GAME = {
    "WGGameSaiga": ("saiga", "saiga", "saiga_dead", "saiga doe", "A long-nosed antelope that bolts at the first sign of a hunter.", "prey", 60, 5.5, 0, "WGMeatSaiga", "WGHide", "WGCutSaigaBackstrap", False),
    "WGGameSaigaBuck": ("saiga", "buck", "saiga_dead", "saiga buck", "A saiga with a fine pair of horns. Quick, and wary.", "prey", 75, 5.5, 0, "WGMeatSaiga", "WGHide", "WGCutSaigaBackstrap", False),
    "WGGameGoat": ("gote", "goat", "goat_dead", "wild goat", "A sure-footed wild goat of the high, cold places.", "prey", 50, 4.5, 0, "WGMeatGoat", "WGPeltGoat", "WGCutGoatShank", False),
    "WGGameBadger": ("badger", "badger", "badger_dead", "badger", "A stocky badger snuffling through the undergrowth.", "prey", 40, 3.5, 0, "WGMeatBadger", "WGHide", None, True),
    "WGGameRaccoon": ("raccoon", "raccoon", "raccoon_dead", "raccoon", "A masked raccoon, quick-handed and quicker to run.", "prey", 30, 4.5, 0, "WGMeatRaccoon", "WGPeltRaccoon", None, False),
    "WGGameMole": ("mole", "mole", "mole_dead", "giant mole", "A huge, half-blind mole that tunnels through the sand.", "prey", 120, 2.5, 0, "WGMeatMole", "WGPeltMole", None, True),
    "WGGameWolf": ("vol", "vv", "vvd", "wolf", "A lean grey wolf. Where there is one, there is a pack.", "predator", 55, 4.8, 8, "WGMeatWolf", "WGPeltWolf", None, False),
    "WGGameDirebear": ("direbear", "direbear", "direbear_dead", "direbear", "A towering bear with a temper to match. Hunt it with friends.", "predator", 220, 3.8, 18, "WGMeatDirebear", "WGPeltDirebear", "WGCutDirebearHaunch", True),
    "WGGameBobcat": ("bobcat", "bobcat", "bobcat_dead", "bobcat", "A tufted wildcat that strikes from cover.", "predator", 45, 4.5, 7, "WGMeatBobcat", "WGPeltBobcat", "WGCutBobcatLoin", False),
    "WGGameHyena": ("Hyena", "yeen", "yeend", "hyena", "A laughing scavenger of the badlands, never alone for long.", "predator", 50, 4.6, 7, "WGMeatHyena", "WGHide", None, False),
    "WGGameBigRat": ("bigrat", "rat", "rat1", "rous", "A rodent of unusual size, fat on the ash-field grubs.", "predator", 40, 4.0, 6, "WGMeatRous", "WGPeltRat", None, False),
    "WGGameMossback": ("boglobster", "mossback", "mossback_dead", "mossback", "A moss-shelled crab of the shallows, all claws and temper.", "predator", 60, 3.0, 8, "WGFoodShellfishMeat", None, None, False),
}

# Meat: id -> (name, description, sprite state, tint, price, traits)
# Traits: tough (fried, it comes out chewy; roast or stew it), fatty (rich, renders fat), gamey (best smoked or seasoned),
# risky (a little toxin raw; cook it through).
MEATS = {
    "WGMeatSaiga": ("saiga venison", "Lean, tender venison. Prized.", "meatsteak", None, 45, []),
    "WGMeatGoat": ("goat meat", "Lean, faintly sweet goat. Tough unless stewed.", "meatsteak", "#D8A090", 30, ["WGMeatTough"]),
    "WGMeatBadger": ("badger meat", "Fatty, rich badger. It makes a stew that lasts.", "pork", None, 30, ["WGMeatFatty"]),
    "WGMeatRaccoon": ("raccoon meat", "Greasy, gamey raccoon. Better smoked.", "cabbitcutlet", "#C8A890", 15, ["WGMeatGamey"]),
    "WGMeatMole": ("mole meat", "Dense, earthy mole, and plenty of it. Tough unless stewed or roasted.", "pork", "#B89080", 25, ["WGMeatTough", "WGMeatFatty"]),
    "WGMeatWolf": ("wolf meat", "Tough, stringy wolf. Stew it or smoke it.", "volfstrip", None, 20, ["WGMeatTough"]),
    "WGMeatDirebear": ("direbear meat", "Rich, heavy bear meat. A feast in itself.", "meatsteak", "#A06050", 50, ["WGMeatFatty"]),
    "WGMeatBobcat": ("bobcat meat", "Lean, pale wildcat meat.", "cabbitcutlet", None, 30, []),
    "WGMeatHyena": ("hyena meat", "Rank, gamey hyena. Cook it through and season it well.", "cabbitcutlet", "#A08070", 15, ["WGMeatGamey", "WGMeatRisky"]),
    "WGMeatRous": ("rous meat", "Poor, risky rat meat. Never eat it underdone.", "cabbitcutlet", "#A0A0A0", 8, ["WGMeatGamey", "WGMeatRisky"]),
}

# Prized cuts, from clean kills only: id -> (name, description, state, tint, price)
CUTS = {
    "WGCutSaigaBackstrap": ("saiga backstrap", "The finest cut of a saiga, from a clean kill.", "meatsteak", "#E08080", 140),
    "WGCutGoatShank": ("goat shank", "A meaty shank for a slow braise.", "meatsteak", "#D09080", 90),
    "WGCutDirebearHaunch": ("direbear haunch", "A huge haunch of direbear. A trophy on a plate.", "meatsteak", "#904040", 300),
    "WGCutBobcatLoin": ("bobcat loin", "A lean, delicate loin.", "cabbitcutlet", "#F0C0B0", 110),
}

# Packs: id -> (game, min, max)
PACKS = {"WGHuntWolfPack": ("WGGameWolf", 2, 4), "WGHuntHyenaPack": ("WGGameHyena", 2, 3), "WGHuntSaigaHerd": ("WGGameSaiga", 2, 4)}

# Where the game lives: wildlife table -> [(game or pack, weight)], by each region's climate.
PLACES = {
    "WGFaunaThrasciasTundra": [("WGGameGoat", 3), ("WGHuntWolfPack", 2), ("WGHuntSaigaHerd", 2)],
    "WGFaunaThrasciasBoreal": [("WGHuntWolfPack", 3), ("WGGameDirebear", 1), ("WGHuntSaigaHerd", 3), ("WGGameBadger", 1)],
    "WGFaunaThrasciasIce": [("WGHuntWolfPack", 1)],
    "WGFaunaVerdaMeadow": [("WGGameBadger", 1), ("WGGameSaigaBuck", 1)],
    "WGFaunaVerdaWoods": [("WGGameRaccoon", 2), ("WGGameBadger", 2), ("WGGameBobcat", 1), ("WGGameDirebear", 0.3)],
    "WGFaunaVerdaShore": [("WGGameMossback", 2), ("WGGameRaccoon", 1)],
    "WGFaunaAerumnaForest": [("WGHuntSaigaHerd", 2), ("WGGameBobcat", 1), ("WGHuntWolfPack", 1)],
    "WGFaunaMerakDunes": [("WGHuntHyenaPack", 2), ("WGGameMole", 1)],
    "WGFaunaMerakBadlands": [("WGHuntHyenaPack", 2), ("WGGameGoat", 2), ("WGGameBobcat", 1)],
    "WGFaunaFervidusAshfields": [("WGGameBigRat", 2)],
    "WGFaunaAsclepiu": [("WGHuntSaigaHerd", 2), ("WGGameRaccoon", 1)],
    "WGFaunaCarcinomaShore": [("WGGameMossback", 3)],
}

# Items: id -> (sheet, state, name, description, price, kind)
ITEMS = {
    "WGFoodGameSteak": ("modular/Neu_Food/icons/cooked/cooked_meat.dmi", "frysteak", "game steak", "A seared steak of wild meat.", 80, "cooked"),
    "WGFoodChewySteak": ("modular/Neu_Food/icons/cooked/cooked_meat.dmi", "frysteak", "chewy steak", "Tough meat, fried too fast. Edible, barely. It wanted a slow roast or the pot.", 30, "cooked"),
    "WGFoodPrimeSteak": ("modular/Neu_Food/icons/cooked/cooked_meat.dmi", "frysteak", "prime steak", "A prized cut, seared just right.", 220, "cooked"),
    "WGFat": ("modular/Neu_Food/icons/raw/raw_meat.dmi", "fat", "animal fat", "A slab of raw fat. Render it in an oven for tallow.", 10, "fat"),
    "WGTallow": ("modular/Neu_Food/icons/raw/raw_meat.dmi", "fat", "tallow", "Rendered fat, for frying and candles.", 25, "fat"),
    "WGOffalHeart": ("_WG/Hunting/props.rsi", "heart", "animal heart", "A game animal's heart. Stewed, it strengthens the blood.", 20, "offal"),
    "WGOffalLiver": ("_WG/Hunting/props.rsi", "liver", "animal liver", "A game animal's liver. With onions, it mends a tired liver.", 20, "offal"),
    "WGFoodJerky": ("modular/Neu_Food/icons/others/preserved_meat.dmi", "jerk1", "jerky", "Strips of smoked, dried meat that keep for a long trip.", 45, "preserved"),
    "WGFoodDriedFish": ("modular/Neu_Food/icons/others/preserved_meat.dmi", "dried_fish_filet", "dried fish", "A fillet of fish, smoked and dried hard.", 40, "preserved"),
    "WGHide": ("icons/roguetown/items/natural.dmi", "hide", "hide", "A raw animal hide. Tan it on a tanning rack.", 30, "pelt"),
    "WGPeltWolf": ("icons/roguetown/items/natural.dmi", "pelt_volf", "wolf pelt", "A thick grey wolf pelt.", 90, "pelt"),
    "WGPeltGoat": ("icons/roguetown/items/natural.dmi", "pelt_gote", "goat pelt", "A shaggy goat pelt.", 50, "pelt"),
    "WGPeltMole": ("icons/roguetown/items/natural.dmi", "pelt_mole", "mole pelt", "A huge, velvety mole pelt.", 120, "pelt"),
    "WGPeltBobcat": ("icons/roguetown/items/natural.dmi", "pelt_bobcat", "bobcat pelt", "A spotted bobcat pelt.", 110, "pelt"),
    "WGPeltDirebear": ("icons/roguetown/items/natural.dmi", "pelt_direbear", "direbear pelt", "An enormous direbear pelt. A hunter's pride.", 400, "pelt"),
    "WGPeltRat": ("icons/roguetown/items/natural.dmi", "pelt_rous", "rous pelt", "A coarse pelt from a rodent of unusual size.", 40, "pelt"),
    "WGPeltRaccoon": ("icons/roguetown/items/natural.dmi", "pelt_raccoon", "raccoon pelt", "A ringed raccoon pelt.", 60, "pelt"),
    "WGLeather": ("icons/roguetown/items/natural.dmi", "leather", "leather", "A sheet of tanned leather.", 70, "leather"),
    "WGBone": ("icons/roguetown/items/natural.dmi", "bone", "bone", "An animal bone. Ground in a nutrient mixer, it makes fine fertilizer.", 10, "bone"),
}


def sheet(path):
    im, desc = read_description(path)
    width, height, states = parse(desc)
    im = im.convert("RGBA")
    columns = im.width // width
    out = {}
    index = 0
    for state in states:
        tiles = []
        for d in range(state["dirs"]):
            i = index + d
            x, y = (i % columns) * width, (i // columns) * height
            tiles.append(im.crop((x, y, x + width, y + height)))
        out.setdefault(state["name"], tiles)
        index += state["dirs"] * state["frames"]
    return out, (width, height)


def write_rsi(path, states, size, source):
    """Writes an RSI whose states have one or four directions, each laid out in a row."""
    path.mkdir(parents=True, exist_ok=True)
    meta = []
    for name, tiles in states.items():
        strip = Image.new("RGBA", (size[0] * len(tiles), size[1]))
        for i, tile in enumerate(tiles):
            strip.paste(tile, (size[0] * i, 0))
        strip.save(path / f"{name}.png")
        meta.append({"name": name, "directions": len(tiles)} if len(tiles) > 1 else {"name": name})
    (path / "meta.json").write_text(json.dumps({"version": 1, "license": "CC-BY-SA-3.0", "copyright": COPYRIGHT.format(file=source),
                                                "size": {"x": size[0], "y": size[1]}, "states": meta}, indent=2) + "\n",
                                    encoding="utf-8", newline="\n")


def place(text, table, entries):
    """Adds the game to a wildlife table's children between WOLFGATE-HUNTING comments, replacing any from a last run."""
    marker = f"  id: {table}\n  table: !type:GroupSelector\n    children:\n"
    i = text.find(marker)
    if i < 0:
        return text, False
    start = i + len(marker)
    text = re.sub(r"    # WOLFGATE-HUNTING START\n.*?    # WOLFGATE-HUNTING END\n", "", text[start:], count=1, flags=re.S)
    block = "    # WOLFGATE-HUNTING START: game for hunting (Hunting module, Tools/_WG/Hunting/port_hunting.py)\n"
    for game, weight in entries:
        if game in PACKS:
            block += f"    - !type:NestedSelector\n      tableId: {game}\n      weight: {weight}\n"
        else:
            block += f"    - id: {game}\n      weight: {weight}\n"
    block += "    # WOLFGATE-HUNTING END\n"
    return None, block


def main():
    tex = ROOT / "Resources/Textures/_WG/Hunting"
    y = ["# Generated by Tools/_WG/Hunting/port_hunting.py from Paragon Moon (art CC BY-SA 3.0). Re-run it; don't edit.", ""]

    sheets = {}
    for gid, (src, alive, dead, name, desc, kind, health, speed, damage, meat, pelt, prized, fatty) in GAME.items():
        butchered = [(meat, max(1, round(health / 22)), 1)]
        if pelt:
            butchered.append((pelt, 1, 1))
        bones = round(health / 55)
        if bones > 0:
            butchered.append(("WGBone", bones, 0.8))
        if fatty:
            butchered.append(("WGFat", max(1, round(health / 80)), 1))
        offal = "" if meat == "WGFoodShellfishMeat" else "    offal: [ WGOffalHeart, WGOffalLiver ]\n"
        game_comp = f"  - type: WGGame\n" + (f"    prizedCut: {prized}\n" if prized else "") + offal
        if src not in sheets:
            sheets[src] = sheet(SOURCE / MOBS / f"{src}.dmi")
        frames, size = sheets[src]
        rsi = f"_WG/Hunting/{gid[6:].lower()}.rsi"
        write_rsi(tex / f"{gid[6:].lower()}.rsi", {"alive": frames[alive], "dead": frames[dead]}, size, f"{MOBS}/{src}.dmi")
        parent = "MobReindeerDoe" if kind == "prey" else "MobBearSpace"
        spawned = "".join(f"    - id: {item}\n      amount: {amount}\n" + (f"      prob: {prob}\n" if prob < 1 else "") for item, amount, prob in butchered)
        # The reindeer base takes cold damage below 260 K; Thrascias's tundra is 180 K. Bear-based game has no Temperature.
        hardy = "  - type: Temperature\n    coldDamageThreshold: 0\n" if parent == "MobReindeerDoe" else ""
        behaviour = """  - type: HTN
    rootTask:
      task: WGVerdaSkittishCompound
    blackboard:
      WGVerdaStartleRange: !type:Single
        7
      WGVerdaFleeRange: !type:Single
        14
      WGVerdaMinFleeRange: !type:Single
        2.5
""" if kind == "prey" else f"""  - type: MeleeWeapon
    damage:
      types:
        Slash: {damage}
"""
        y.append(f"""- type: entity
  parent: {parent}
  id: {gid}
  name: {name}
  description: {desc} Butcher it with a knife.
  components:
  - type: Sprite
    sprite: {rsi}
    layers:
    - map: [ "enum.DamageStateVisualLayers.Base" ]
      state: alive
  - type: DamageStateVisuals
    states:
      Alive:
        Base: alive
      Critical:
        Base: dead
      Dead:
        Base: dead
  - type: MobThresholds
    thresholds:
      0: Alive
      {health}: Dead
  - type: MovementSpeedModifier
    baseWalkSpeed: {round(speed * 0.55, 2)}
    baseSprintSpeed: {speed}
  - type: Butcherable
    spawned:
{spawned}{game_comp}  # Native wildlife breathes its own world's air, thin or not, and bears its cold.
  - type: BreathingImmunity
{hardy}{behaviour}""")

    for pid, (game, low, high) in PACKS.items():
        y.append(f"- type: entityTable\n  id: {pid}\n  table: !type:AllSelector\n    children:\n    - id: {game}\n      rolls: !type:RangeNumberSelector\n        range: {low}, {high}\n")

    item_states = {}
    raw = sheet(SOURCE / "modular/Neu_Food/icons/raw/raw_meat.dmi")[0]
    for mid, (name, desc, st, tint, price, traits) in MEATS.items():
        item_states[mid.lower()] = [raw[st][0]]
        toxin = "        - ReagentId: Toxin\n          Quantity: 2\n" if "WGMeatRisky" in traits else ""
        color = f"    color: \"{tint}\"\n" if tint else ""
        tags = ", ".join(["WGGameMeat", "WGFarmGood"] + traits)
        y.append(f"""- type: entity
  parent: FoodBase
  id: {mid}
  name: {name}
  description: {desc}
  components:
  - type: Sprite
    sprite: _WG/Hunting/items.rsi
    state: {mid.lower()}
{color}  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: 14
        reagents:
        - ReagentId: Protein
          Quantity: 5
        - ReagentId: Nutriment
          Quantity: 4
{toxin}  - type: Tag
    tags: [ {tags} ]
  - type: StaticPrice
    price: {price}
""")
    for cid, (name, desc, st, tint, price) in CUTS.items():
        item_states[cid.lower()] = [raw[st][0]]
        y.append(f"""- type: entity
  parent: FoodBase
  id: {cid}
  name: {name}
  description: {desc}
  components:
  - type: Sprite
    sprite: _WG/Hunting/items.rsi
    state: {cid.lower()}
    color: "{tint}"
  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: 16
        reagents:
        - ReagentId: Protein
          Quantity: 8
        - ReagentId: Nutriment
          Quantity: 6
  - type: Tag
    tags: [ WGPrizedCut, WGFarmGood ]
  - type: StaticPrice
    price: {price}
""")

    for iid, (src, st, name, desc, price, kind) in ITEMS.items():
        if src.endswith(".rsi"):
            y.append(f"""- type: entity
  parent: FoodBase
  id: {iid}
  name: {name}
  description: {desc}
  components:
  - type: Sprite
    sprite: {src}
    state: {st}
  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: 8
        reagents:
        - ReagentId: Protein
          Quantity: 4
  - type: Tag
    tags: [ WGOffal ]
  - type: StaticPrice
    price: {price}
""")
            continue
        frames, _ = sheet(SOURCE / src) if src not in sheets else sheets[src]
        sheets.setdefault(src, (frames, (32, 32)))
        item_states[iid.lower()] = [frames[st][0]]
        meal = {
            "WGFoodGameSteak": "  - type: WGMeal\n    traits: [ Hearty, Fortifying, Brawny ]\n    strength: 1.0\n    minutes: 12\n",
            "WGFoodChewySteak": "  - type: WGMeal\n    traits: [ Hearty ]\n    strength: 0.5\n    minutes: 8\n",
            "WGFoodPrimeSteak": "  - type: WGMeal\n    traits: [ Hearty, Brawny, Energising ]\n    strength: 1.5\n    minutes: 15\n",
            "WGFoodJerky": "  - type: WGMeal\n    traits: [ Hearty, Steady ]\n    strength: 0.6\n    minutes: 20\n    neverStale: true\n",
            "WGFoodDriedFish": "  - type: WGMeal\n    traits: [ Fresh, Steady ]\n    strength: 0.6\n    minutes: 20\n    neverStale: true\n",
        }.get(iid, "")
        if kind in ("meat", "cooked", "preserved", "fat"):
            tags = {"meat": "WGGameMeat", "cooked": "WGHomeCooking", "preserved": "WGHomeCooking", "fat": "WGFat"}[kind]
            nutriment = {"meat": 4, "cooked": 8, "preserved": 6, "fat": 3}[kind]
            y.append(f"""- type: entity
  parent: FoodBase
  id: {iid}
  name: {name}
  description: {desc}
  components:
  - type: Sprite
    sprite: _WG/Hunting/items.rsi
    state: {iid.lower()}
  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: {nutriment + 6}
        reagents:
        - ReagentId: Protein
          Quantity: 5
        - ReagentId: Nutriment
          Quantity: {nutriment}
  - type: Tag
    tags: [ {tags}, WGFarmGood ]
  - type: StaticPrice
    price: {price}
{meal}""")
        else:
            tags = {"pelt": "WGPelt", "leather": "WGFarmGood", "bone": "WGFarmGood"}[kind]
            y.append(f"""- type: entity
  parent: BaseItem
  id: {iid}
  name: {name}
  description: {desc}
  components:
  - type: Sprite
    sprite: _WG/Hunting/items.rsi
    state: {iid.lower()}
  - type: Item
    size: {"Normal" if kind == "pelt" else "Small"}
  - type: Tag
    tags: [ {tags} ]
  - type: StaticPrice
    price: {price}
""")
    write_rsi(tex / "items.rsi", item_states, (32, 32), "icons/roguetown/items/natural.dmi and modular/Neu_Food/icons")

    y.append("""# Butchery, cooking and preserving.

- type: wgChopRecipe
  id: WGChopGameMeat
  tags: [ WGGameMeat ]
  result: WGFoodMeatMince
  count: 2

- type: wgCookRecipe
  id: WGCookGameSteakFry
  method: Fry
  tags: [ WGGameMeat ]
  result: WGFoodGameSteak
  seconds: 25

# Tough meat fried fast comes out chewy; roast it or stew it instead.
- type: wgCookRecipe
  id: WGCookToughFry
  method: Fry
  tags: [ WGMeatTough ]
  result: WGFoodChewySteak
  seconds: 25
  priority: 10

- type: wgCookRecipe
  id: WGCookPrimeFry
  method: Fry
  tags: [ WGPrizedCut ]
  result: WGFoodPrimeSteak
  seconds: 30
  priority: 20

- type: wgCookRecipe
  id: WGCookPrimeBake
  method: Bake
  tags: [ WGPrizedCut ]
  result: WGFoodPrimeSteak
  seconds: 50
  priority: 20

# Rendering fat into tallow.
- type: wgCookRecipe
  id: WGCookRenderFat
  method: Bake
  inputs: [ WGFat ]
  result: WGTallow
  seconds: 30

# Offal stews: liver and onion, and the hunter's heart stew.
- type: reagent
  id: WGSoupLiverOnion
  name: reagent-name-wg-soup-liveronion
  desc: reagent-desc-wg-soup-liveronion
  group: Foods
  physicalDesc: reagent-physical-desc-wg-soup
  flavor: savory
  color: "#6A3A28"
  metabolisms:
    Food:
      effects:
      - !type:SatiateHunger
        factor: 2
      - !type:WGMealBuff
        traits: [ Hearty, Fortifying ]
        strength: 1.25
        minutes: 15
      - !type:WGMendOrgans
        amount: 0.12
        favours: [ liver ]
  pricePerUnit: 4

- type: reagent
  id: WGSoupHuntersHeart
  name: reagent-name-wg-soup-huntersheart
  desc: reagent-desc-wg-soup-huntersheart
  group: Foods
  physicalDesc: reagent-physical-desc-wg-soup
  flavor: savory
  color: "#7A2A20"
  metabolisms:
    Food:
      effects:
      - !type:SatiateHunger
        factor: 2
      - !type:WGMealBuff
        traits: [ Hearty, Brawny, Warming ]
        strength: 1.25
        minutes: 15
      - !type:WGMendOrgans
        amount: 0.12
        favours: [ heart ]
  pricePerUnit: 4

- type: wgStewRecipe
  id: WGStewLiverOnion
  combine:
  - ids: [ WGOffalLiver ]
    count: 1
  - ids: [ WGFoodOnionSliced ]
    count: 1
  soup: WGSoupLiverOnion
  seconds: 80

- type: wgStewRecipe
  id: WGStewHuntersHeart
  combine:
  - ids: [ WGOffalHeart ]
    count: 1
  - ids: [ WGFoodPotatoSliced ]
    count: 1
  soup: WGSoupHuntersHeart
  seconds: 90

- type: wgCookRecipe
  id: WGCookGameSteakBake
  method: Bake
  tags: [ WGGameMeat ]
  result: WGFoodGameSteak
  seconds: 40

- type: wgProcessRecipe
  id: WGSmokeJerky
  machine: SmokingRack
  tags: [ WGGameMeat ]
  inputs: [ FoodMeat, FoodMeatBear ]
  minutes: 20
  keepQuality: false
  outputs:
  - id: WGFoodJerky
    count: 2

- type: wgProcessRecipe
  id: WGSmokeFish
  machine: SmokingRack
  inputs: [ WGFoodFishFillet ]
  minutes: 15
  keepQuality: false
  outputs:
  - id: WGFoodDriedFish

- type: wgProcessRecipe
  id: WGTanLeather
  machine: TanningRack
  tags: [ WGPelt ]
  minutes: 30
  keepQuality: false
  outputs:
  - id: WGLeather
    count: 2

- type: wgProcessRecipe
  id: WGMixBoneMeal
  machine: NutrientMixer
  inputs: [ WGBone ]
  minutes: 10
  keepQuality: false
  outputs:
  - id: WGFertilizerQuality

- type: entity
  parent: WGProcessorBase
  id: WGHuntSmokingRack
  name: smoking rack
  description: Smokes meat into jerky and fish into dried fish that keep for a long trip. No power needed.
  components:
  - type: Construction
    graph: WGHuntSmokingRackGraph
    node: built
  - type: Sprite
    sprite: _WG/Hunting/props.rsi
    layers:
    - state: smoking_rack
    - state: smoking_rack_working
      map: [ "drying" ]
      visible: false
  - type: Appearance
  - type: GenericVisualizer
    visuals:
      enum.WGProcessorVisuals.Working:
        drying:
          True: { visible: true }
          False: { visible: false }
  - type: WGProcessor
    machine: SmokingRack
  - type: StaticPrice
    price: 60
  - type: GuideHelp
    guides:
    - WGHunting

- type: entity
  parent: WGProcessorBase
  id: WGHuntTanningRack
  name: tanning rack
  description: Tans pelts and hides into leather.
  components:
  - type: Construction
    graph: WGHuntTanningRackGraph
    node: built
  - type: Sprite
    sprite: _WG/Hunting/props.rsi
    state: tanning_rack
  - type: WGProcessor
    machine: TanningRack
  - type: StaticPrice
    price: 60
  - type: GuideHelp
    guides:
    - WGHunting

- type: Tag
  id: WGGameMeat

- type: Tag
  id: WGMeatTough

- type: Tag
  id: WGMeatFatty

- type: Tag
  id: WGMeatGamey

- type: Tag
  id: WGMeatRisky

- type: Tag
  id: WGPrizedCut

- type: Tag
  id: WGOffal

- type: Tag
  id: WGFat

- type: Tag
  id: WGPelt

- type: wgFarmOrder
  id: WGOrderPelts
  name: wg-order-name-pelts
  tags: [ WGPelt ]
  minCount: 2
  maxCount: 6
  price: 100
  bonus: 600

- type: wgFarmOrder
  id: WGOrderJerky
  name: wg-order-name-jerky
  items: [ WGFoodJerky ]
  minCount: 4
  maxCount: 10
  price: 60
  bonus: 400
""")
    out = ROOT / "Resources/Prototypes/_WG/Hunting"
    out.mkdir(parents=True, exist_ok=True)
    (out / "hunting.yml").write_text("\n".join(y), encoding="utf-8", newline="\n")

    # Spread the game over the planets' wildlife tables.
    placed = set()
    for path in list((ROOT / "Resources/Prototypes/_WG/Planets").rglob("*.yml")):
        raw = path.read_bytes().decode("utf-8")
        nl = "\r\n" if "\r\n" in raw else "\n"
        text = raw.replace("\r\n", "\n")
        changed = False
        for table, entries in PLACES.items():
            marker = f"  id: {table}\n  table: !type:GroupSelector\n    children:\n"
            i = text.find(marker)
            if i < 0:
                continue
            start = i + len(marker)
            rest = re.sub(r"\A    # WOLFGATE-HUNTING START[^\n]*\n.*?    # WOLFGATE-HUNTING END\n", "", text[start:], count=1, flags=re.S)
            _, block = place(text, table, entries)
            text = text[:start] + block + rest
            placed.add(table)
            changed = True
        if changed:
            path.write_bytes(text.replace("\n", nl).encode("utf-8"))
    missing = set(PLACES) - placed
    print(f"{len(GAME)} game, {len(ITEMS)} items, placed in {len(placed)} tables" + (f"; not found: {sorted(missing)}" if missing else ""))


if __name__ == "__main__":
    main()
