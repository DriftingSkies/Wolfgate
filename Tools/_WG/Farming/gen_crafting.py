"""Writes the construction-menu recipes for every farming, cooking, fishing and hunting machine.

Run from the repository root: python Tools/_WG/Farming/gen_crafting.py
Each machine is built from materials in the construction menu (category Machines); costs are set below by kind.
"""
import re
from pathlib import Path

PROTO = Path("Resources/Prototypes/_WG")

WOOD = [("WoodPlank", 8)]
JAR = [("Glass", 4)]
MACHINE = [("Steel", 6), ("Cable", 3)]
MACHINE_GLASS = [("Steel", 6), ("Glass", 2), ("Cable", 3)]

# id -> (module, materials, wall-mounted)
MACHINES = {
    "WGFarmKeg": ("Farming", WOOD, False),
    "WGFarmPreservesJar": ("Farming", JAR, False),
    "WGFarmCheesePress": ("Farming", MACHINE, False),
    "WGFarmMayoMachine": ("Farming", MACHINE, False),
    "WGFarmLoom": ("Farming", MACHINE, False),
    "WGFarmMill": ("Farming", MACHINE, False),
    "WGFarmComposter": ("Farming", WOOD, False),
    "WGFarmFertilizerBarrel": ("Farming", WOOD, False),
    "WGFarmNutrientMixer": ("Farming", MACHINE_GLASS, False),
    "WGFarmCask": ("Farming", WOOD, False),
    "WGFarmBeeHive": ("Farming", [("WoodPlank", 6)], False),
    "WGFarmMushroomLog": ("Farming", [("WoodPlank", 4)], False),
    "WGFarmComputeRack": ("Farming", [("Steel", 8), ("Glass", 2), ("Cable", 6)], False),
    "WGFarmDigester": ("Farming", MACHINE_GLASS, False),
    "WGFarmOreWasher": ("Farming", MACHINE, False),
    "WGFarmGeodeCracker": ("Farming", MACHINE, False),
    "WGFarmGlassKiln": ("Farming", MACHINE, False),
    "WGFarmSeedMaker": ("Farming", MACHINE_GLASS, False),
    "WGFarmScarecrow": ("Farming", [("WoodPlank", 4)], False),
    "WGSprinklerBasic": ("Farming", [("Steel", 2)], False),
    "WGSprinklerQuality": ("Farming", [("Steel", 3), ("Cable", 1)], False),
    "WGSprinklerAdvanced": ("Farming", [("Steel", 4), ("Glass", 1), ("Cable", 2)], False),
    "WGFarmPackingTable": ("Farming", [("WoodPlank", 6)], False),
    "WGFarmOrderBoard": ("Farming", [("Steel", 3), ("Glass", 2), ("Cable", 1)], False),
    "WGRainCatcher": ("Farming", [("WoodPlank", 4), ("Steel", 2)], False),
    "WGMoistureVaporator": ("Farming", MACHINE_GLASS, False),
    "WGSnowMelter": ("Farming", MACHINE, False),
    "WGGrowLamp": ("Farming", [("Steel", 1), ("Glass", 1), ("Cable", 1)], True),
    "WGHearthStove": ("Cooking", [("Steel", 8), ("Cable", 2)], False),
    "WGPrepBoard": ("Cooking", [("WoodPlank", 3)], False),
    "WGCampStove": ("Cooking", [("Steel", 3)], False),
    "WGCuttingBoard": ("Cooking", [("WoodPlank", 2)], False),
    "WGFishPond": ("Fishing", [("WoodPlank", 6), ("Glass", 2)], False),
    "WGHuntSmokingRack": ("Hunting", [("WoodPlank", 6)], False),
    "WGHuntTanningRack": ("Hunting", [("WoodPlank", 6)], False),
}

# The item each construction material is refunded as when a machine is pried apart.
REFUND = {"Steel": "SheetSteel1", "Glass": "SheetGlass1", "WoodPlank": "MaterialWoodPlank1", "Cable": "CableApcStack1"}

ICON_FALLBACK = {"WGGrowLamp": ("Structures/Wallmounts/Lighting/light_small.rsi", "base")}


def entities():
    found = {}
    for path in PROTO.glob("*/*.yml"):
        for block in re.split(r"\n(?=- type: )", path.read_text(encoding="utf-8")):
            m = re.search(r"\n  id: (\S+)", block)
            if not block.lstrip().startswith("- type: entity") or not m:
                continue
            name = re.search(r"\n  name: (.+)", block)
            desc = re.search(r"\n  description: (.+)", block)
            parent = re.search(r"\n  parent: (\S+)", block)
            sprite = re.search(r"sprite: (\S+)", block)
            state = re.search(r"state: (\S+)", block)
            found[m.group(1)] = dict(name=name and name.group(1), desc=desc and desc.group(1), parent=parent and parent.group(1),
                                     sprite=sprite and sprite.group(1), state=state and state.group(1))
    return found


def resolve(ents, eid, key):
    while eid in ents:
        if ents[eid][key]:
            return ents[eid][key]
        eid = ents[eid]["parent"]
    return None


def main():
    ents = entities()
    out = {}
    for eid, (module, materials, wall) in MACHINES.items():
        assert eid in ents, eid
        name = resolve(ents, eid, "name")
        desc = (resolve(ents, eid, "desc") or "").replace('"', "'")
        sprite, state = ICON_FALLBACK.get(eid, (resolve(ents, eid, "sprite"), resolve(ents, eid, "state")))
        steps = "".join(f"      - material: {m}\n        amount: {a}\n        doAfter: 2\n" for m, a in materials)
        conditions = "  - !type:WallmountCondition\n" if wall else "  - !type:TileNotBlocked\n"
        # Pried apart, it drops whatever it held and gives its materials back.
        refund = "".join(f"      - !type:SpawnPrototype\n        prototype: {REFUND[m]}\n        amount: {a}\n" for m, a in materials)
        out.setdefault(module, []).append(f"""- type: constructionGraph
  id: {eid}Graph
  start: start
  graph:
  - node: start
    edges:
    - to: built
      steps:
{steps}  - node: built
    entity: {eid}
    edges:
    - to: start
      completed:
      - !type:EmptyAllContainers {{}}
{refund}      - !type:DeleteEntity {{}}
      steps:
      - tool: Prying
        doAfter: 3

- type: construction
  name: {name}
  id: {eid}Recipe
  graph: {eid}Graph
  startNode: start
  targetNode: built
  category: construction-category-machines
  description: "{desc}"
  icon:
    sprite: {sprite}
    state: {state}
  objectType: Structure
  placementMode: SnapgridCenter
  canRotate: {"true" if wall else "false"}
  canBuildInImpassable: {"true" if wall else "false"}
  conditions:
{conditions}""")

    for module, blocks in out.items():
        header = ("# Generated by Tools/_WG/Farming/gen_crafting.py: construction-menu recipes for this module's machines.\n"
                  "# Edit the costs there and rerun.\n\n")
        (PROTO / module / "crafting.yml").write_text(header + "\n".join(blocks), encoding="utf-8")
        print(module, len(blocks))


if __name__ == "__main__":
    main()
