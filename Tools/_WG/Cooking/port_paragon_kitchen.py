"""Ports Roguetown-style cooking content from the owner's Paragon Moon project (art CC BY-SA 3.0).

Usage: python Tools/_WG/Cooking/port_paragon_kitchen.py [path/to/Paragon-Moon]

Writes the sprites under Resources/Textures/_WG/Cooking, and Resources/Prototypes/_WG/Cooking/kitchen.yml with the
hearth stove, pan, pots, bowls and prep board; the raw and finished dishes; their prep, fry and bake recipes; and the
stews (wgStewRecipe) with their soup reagents. Names go to Resources/Locale/en-US/_WG/Cooking/kitchen.ftl. Re-run it
after changing the tables below; it overwrites what it wrote.
"""
import json
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools" / "_WG" / "Wolfmed"))
from dmi_extract import parse, read_description  # noqa: E402

SOURCE = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT.parent / "Paragon-Moon"
TEXTURES = ROOT / "Resources" / "Textures" / "_WG" / "Cooking"
PROTOTYPES = ROOT / "Resources" / "Prototypes" / "_WG" / "Cooking"
LOCALE = ROOT / "Resources" / "Locale" / "en-US" / "_WG" / "Cooking"
FOOD = "modular/Neu_Food/icons"
COPYRIGHT = "Taken from Paragon Moon (Roguetown lineage), {file}; adapted for Wolfgate."

# Finished dishes: id -> (sheet, state, name, description, nutriment, price, fine)
DISHES = {
    "WGFoodRaspberryCake": ("cooked/cooked_cakes", "berrycake", "raspberry cake", "A sponge cake layered with raspberries.", 20, 340, True),
    "WGFoodBlackberryCake": ("cooked/cooked_cakes", "blackberrycake", "blackberry cake", "A dark, juicy blackberry cake.", 20, 340, True),
    "WGFoodHoneyCake": ("cooked/cooked_cakes", "honeycake", "honey cake", "A golden cake sweetened with outpost honey.", 20, 400, True),
    "WGFoodFruitCake": ("cooked/cooked_cakes", "fruitcake", "fruit cake", "A dense cake packed with pear, plum and tangerine.", 24, 450, True),
    "WGFoodLimeCake": ("cooked/cooked_cakes", "limecake", "lime cake", "A sharp, bright lime cake.", 20, 320, True),
    "WGFoodStrawberryPie": ("cooked/cooked_pies", "pie", "strawberry pie", "A lattice pie full of strawberries.", 18, 300, True),
    "WGFoodQuiche": ("cooked/cooked_cakes", "quiche", "quiche", "Egg and cheese baked in a pastry shell.", 16, 260, True),
    "WGFoodScallopedPotatoes": ("cooked/cooked_veggies", "scalloped_potatoes", "scalloped potatoes", "Thin potatoes baked in cheese.", 14, 160, False),
    "WGFoodStuffedEggplant": ("cooked/cooked_veggies", "stuffedeggplantcheese", "stuffed eggplant", "Eggplant baked with tomato and cheese.", 14, 180, False),
    "WGFoodOmelette": ("cooked/cooked_egg", "omelette", "omelette", "A folded omelette with cheese and onion.", 12, 120, False),
    "WGFoodFriedCabbage": ("cooked/cooked_veggies", "cabbage_fried", "fried cabbage", "Cabbage fried soft in the pan.", 8, 50, False),
    "WGFoodFriedPotato": ("cooked/cooked_veggies", "potato_fried", "fried potato", "Potato fried crisp at the edges.", 8, 45, False),
    "WGFoodFriedOnion": ("cooked/cooked_veggies", "onion_fried", "fried onion", "Onion fried sweet and brown.", 5, 30, False),
    "WGFoodBakedPotato": ("cooked/cooked_veggies", "potato_baked", "baked potato", "A potato baked soft in its skin.", 8, 45, False),
    "WGFoodRoastTurnip": ("cooked/cooked_veggies", "butter_potato", "roast turnip", "Turnip roasted until tender.", 8, 45, False),
    "WGFoodRoastSeeds": ("cooked/cooked_veggies", "roastseeds", "roasted seeds", "A handful of roasted sunflower seeds.", 6, 50, False),
    "WGFoodFriedEgg": ("cooked/cooked_egg", "friedegg", "fried egg", "An egg fried sunny side up.", 6, 30, False),
}

# Meal buffs (Cooking WGMeal / WGMealBuff): id -> (traits, strength, minutes, never stale)
MEALS = {
    "WGFoodRaspberryCake": (["Energising", "Fresh"], 1.0, 12, False),
    "WGFoodBlackberryCake": (["Energising", "Fresh"], 1.0, 12, False),
    "WGFoodHoneyCake": (["Energising", "Hearty"], 1.1, 12, False),
    "WGFoodFruitCake": (["Fresh", "Energising", "Hearty"], 1.1, 14, False),
    "WGFoodLimeCake": (["Energising", "Cooling"], 1.0, 12, False),
    "WGFoodStrawberryPie": (["Energising", "Fresh"], 1.0, 12, False),
    "WGFoodQuiche": (["Hearty", "Fortifying", "Steady"], 1.1, 12, False),
    "WGFoodScallopedPotatoes": (["Hearty", "Warming"], 1.0, 12, False),
    "WGFoodStuffedEggplant": (["Fresh", "Hearty"], 1.0, 12, False),
    "WGFoodOmelette": (["Fortifying", "Hearty"], 1.0, 10, False),
    "WGFoodFriedCabbage": (["Fresh"], 0.8, 8, False),
    "WGFoodFriedPotato": (["Hearty"], 0.8, 8, False),
    "WGFoodFriedOnion": (["Warming"], 0.7, 8, False),
    "WGFoodBakedPotato": (["Hearty", "Warming"], 0.9, 10, False),
    "WGFoodRoastTurnip": (["Hearty"], 0.8, 10, False),
    "WGFoodRoastSeeds": (["Energising", "Steady"], 0.7, 8, False),
    "WGFoodFriedEgg": (["Fortifying"], 0.8, 8, False),
}

# Soup buffs: stew id -> traits (stews of several ingredients are 25% stronger)
SOUP_TRAITS = {
    "Porridge": ["Hearty", "Warming"], "Oatmeal": ["Hearty", "Warming"], "Congee": ["Hearty", "Fresh"],
    "PotatoSoup": ["Hearty", "Warming"], "OnionSoup": ["Warming"], "CabbageSoup": ["Fresh", "Warming"],
    "TurnipSoup": ["Fresh"], "BerryStew": ["Energising", "Fresh", "Cooling"], "CheeseSoup": ["Hearty", "Fortifying"],
    "EggSoup": ["Fortifying", "Fresh"], "MushroomBroth": ["Warming", "Fresh"], "FishBroth": ["Fresh", "Cooling"],
    "MeatStew": ["Hearty", "Fortifying", "Brawny"], "VegetableStew": ["Fresh", "Hearty", "Warming"],
    "HeartyStew": ["Hearty", "Fortifying", "Warming"], "FishChowder": ["Hearty", "Fresh", "Steady"],
    "SeafoodBisque": ["Fresh", "Fortifying", "Cooling"], "CheeseOnionSoup": ["Warming", "Hearty"],
    "CreamOfMushroom": ["Warming", "Fresh", "Hearty"], "FruitPorridge": ["Hearty", "Energising"],
    "BorschtSoup": ["Fresh", "Warming"],
}


def meal_component(traits, strength, minutes, never):
    lines = f"  - type: WGMeal\n    traits: [ {', '.join(traits)} ]\n    strength: {strength}\n    minutes: {minutes}\n"
    return lines + ("    neverStale: true\n" if never else "")


# Raw dishes made on the prep board: id -> (sheet, state, name, ingredients [(ids, tag, count)], cooks to, method, seconds)
RAW = {
    "WGRawRaspberryCake": ("raw/raw_cakes", "cake", "raw raspberry cake", [(["FoodCakeBatter"], None, 1), (["WGFoodRaspberry"], None, 3)], "WGFoodRaspberryCake", "Bake", 60),
    "WGRawBlackberryCake": ("raw/raw_cakes", "cake", "raw blackberry cake", [(["FoodCakeBatter"], None, 1), (["WGFoodBlackberry"], None, 3)], "WGFoodBlackberryCake", "Bake", 60),
    "WGRawHoneyCake": ("raw/raw_cakes", "honeycake", "raw honey cake", [(["FoodCakeBatter"], None, 1), (["WGHoney"], None, 1)], "WGFoodHoneyCake", "Bake", 60),
    "WGRawFruitCake": ("cooked/cooked_cakes", "fruitcakeuncook", "raw fruit cake", [(["FoodCakeBatter"], None, 1), (["WGFoodPear"], None, 1), (["WGFoodPlum"], None, 1), (["WGFoodTangerine"], None, 1)], "WGFoodFruitCake", "Bake", 75),
    "WGRawLimeCake": ("raw/raw_cakes", "cake", "raw lime cake", [(["FoodCakeBatter"], None, 1), (["FoodLime"], None, 2)], "WGFoodLimeCake", "Bake", 60),
    "WGRawStrawberryPie": ("raw/raw_pies", "pieuncooked", "raw strawberry pie", [(["FoodDough"], None, 1), (["WGFoodStrawberry"], None, 3)], "WGFoodStrawberryPie", "Bake", 60),
    "WGRawQuiche": ("raw/raw_pies", "pieuncooked", "raw quiche", [(["FoodDoughSlice"], None, 1), (["FoodEgg"], None, 2), (["FoodCheeseSlice"], None, 1)], "WGFoodQuiche", "Bake", 50),
    "WGRawScallopedPotatoes": ("raw/raw_veggies", "potato_sliced", "raw scalloped potatoes", [(["WGFoodPotatoSliced"], None, 3), (["FoodCheeseSlice"], None, 1)], "WGFoodScallopedPotatoes", "Bake", 45),
    "WGRawStuffedEggplant": ("cooked/cooked_veggies", "eggplantrawtom", "raw stuffed eggplant", [(["FoodEggplant"], None, 1), (["FoodTomato"], None, 1), (["FoodCheeseSlice"], None, 1)], "WGFoodStuffedEggplant", "Bake", 45),
    "WGRawOmelette": ("cooked/cooked_egg", "rawegg", "omelette mix", [(["FoodEgg"], None, 2), (["FoodCheeseSlice"], None, 1), (["FoodOnion"], None, 1)], "WGFoodOmelette", "Fry", 25),
}

# Single ingredients straight into the pan or oven: (method, inputs, result, seconds)
SIMPLE = [
    ("Fry", ["FoodCabbage"], "WGFoodFriedCabbage", 20),
    ("Fry", ["FoodPotato"], "WGFoodFriedPotato", 25),
    ("Fry", ["WGFoodPotatoSliced"], "WGFoodFriedPotato", 15),
    ("Fry", ["WGFoodOnionSliced"], "WGFoodFriedOnion", 10),
    ("Fry", ["FoodOnion"], "WGFoodFriedOnion", 15),
    ("Fry", ["FoodEgg"], "WGFoodFriedEgg", 15),
    ("Fry", ["FoodMeat"], "FoodMeatCooked", 30),
    ("Fry", ["WGFoodSunflower"], "WGFoodRoastSeeds", 15),
    ("Bake", ["FoodPotato"], "WGFoodBakedPotato", 40),
    ("Bake", ["WGFoodTurnip"], "WGFoodRoastTurnip", 40),
    ("Bake", ["FoodMeat"], "FoodMeatCooked", 45),
]

# Chopped on the prep board with a knife: id -> (sheet, state, name, chopped from, count)
SLICES = {
    "WGFoodPotatoSliced": ("@_WG/Cooking/props.rsi", "potato_wedges", "potato wedges", ["FoodPotato"], 2),
    "WGFoodOnionSliced": ("raw/raw_veggies", "onion_sliced", "sliced onion", ["FoodOnion"], 2),
    "WGFoodCabbageSliced": ("raw/raw_veggies", "cabbage_sliced", "shredded cabbage", ["FoodCabbage"], 2),
    "WGFoodTurnipSliced": ("raw/raw_veggies", "turnip_sliced", "sliced turnip", ["WGFoodTurnip"], 2),
    "WGFoodCucumberSliced": ("raw/raw_veggies", "cucumber_slices", "cucumber slices", ["WGFoodCucumber"], 2),
    "WGFoodGarlicClove": ("raw/raw_veggies", "garlic_clove", "garlic clove", ["FoodGarlic"], 3),
    "WGFoodMeatMince": ("raw/raw_meat", "meatmince", "minced meat", ["FoodMeat"], 2),
    # Paragon has no sliced sprite for these; an "@" sheet is an upstream RSI used as it is.
    "WGFoodCarrotSliced": ("@Objects/Specific/Hydroponics/carrot.rsi", "produce", "sliced carrot", ["FoodCarrot"], 3),
    "WGFoodTomatoSliced": ("@Objects/Specific/Hydroponics/tomato.rsi", "produce", "sliced tomato", ["FoodTomato"], 3),
    "WGFoodEggplantSliced": ("@Objects/Specific/Hydroponics/eggplant.rsi", "produce", "sliced eggplant", ["FoodEggplant"], 3),
    "WGFoodChiliChopped": ("@Objects/Specific/Hydroponics/chili.rsi", "produce", "chopped chili", ["FoodChiliPepper"], 2),
    "WGFoodAppleSliced": ("@_WG/Cooking/props.rsi", "apple_sliced", "apple slices", ["FoodApple"], 4),
    "WGFoodLemonWedge": ("@Objects/Specific/Hydroponics/lemon.rsi", "produce", "lemon wedge", ["FoodLemon"], 4),
    "WGFoodLimeWedge": ("@Objects/Specific/Hydroponics/lime.rsi", "produce", "lime wedge", ["FoodLime"], 4),
    "WGFoodCornKernels": ("@Objects/Specific/Hydroponics/corn.rsi", "produce", "corn kernels", ["FoodCorn"], 2),
}

BERRIES = ["FoodBerries", "WGFoodStrawberry", "WGFoodBlackberry", "WGFoodRaspberry"]

# Stews, after Paragon's stew_recipes.dm (vegetables and meat go in chopped, as there):
# id -> (name, description, colour, inputs, tags, seconds, order, combine [(ids, count)])
# A stew with `combine` is made when all its ingredients are stewing in one pot together.
STEWS = {
    "Porridge": ("porridge", "A thick, plain porridge of wheat.", "#D8C8A0", ["WheatBushel"], [], 30, 0, []),
    "Oatmeal": ("oatmeal", "Warm, filling oatmeal.", "#C8B080", ["OatBushel"], [], 30, 0, []),
    "Congee": ("congee", "A soft rice congee.", "#E8E0C8", ["RiceBushel"], [], 30, 0, []),
    "PotatoSoup": ("potato soup", "A creamy potato soup.", "#D8C080", ["WGFoodPotatoSliced"], [], 60, 0, []),
    "OnionSoup": ("onion soup", "A sweet, brown onion soup.", "#A87040", ["WGFoodOnionSliced"], [], 60, 0, []),
    "CabbageSoup": ("cabbage soup", "A light cabbage soup.", "#A0C070", ["WGFoodCabbageSliced"], [], 60, 0, []),
    "TurnipSoup": ("turnip soup", "An earthy turnip soup.", "#D0B0C0", ["WGFoodTurnipSliced"], [], 60, 0, []),
    "BerryStew": ("berry stew", "A sweet stew of stewed berries.", "#A02848", BERRIES, [], 45, 0, []),
    "CheeseSoup": ("cheese soup", "A rich, stringy cheese soup.", "#E8C040", ["FoodCheese", "FoodCheeseSlice"], [], 45, 0, []),
    "EggSoup": ("egg drop soup", "Thin soup with ribbons of egg.", "#E8D890", ["FoodEgg"], [], 40, 0, []),
    "MushroomBroth": ("mushroom broth", "A dark, savoury mushroom broth.", "#6A5040", ["FoodMushroom"], [], 45, 0, []),
    "FishBroth": ("fish broth", "A clear broth of fish.", "#C8C0A0", ["WGFoodFishFillet"], [], 50, 0, []),
    "MeatStew": ("meat stew", "A hearty meat stew.", "#7A3A20", ["WGFoodMeatMince"], [], 75, 100, []),
    # Stews of several ingredients.
    "VegetableStew": ("vegetable stew", "A thick stew of potato, onion and cabbage.", "#A09050", [], [], 90, 0,
                      [(["WGFoodPotatoSliced"], 1), (["WGFoodOnionSliced"], 1), (["WGFoodCabbageSliced"], 1)]),
    "HeartyStew": ("hearty stew", "Meat, potato and onion stewed until they fall apart. It sticks to your ribs.", "#6A3018", [], [], 120, 0,
                   [(["WGFoodMeatMince"], 1), (["WGFoodPotatoSliced"], 1), (["WGFoodOnionSliced"], 1)]),
    "FishChowder": ("fish chowder", "A creamy chowder of fish, potato and onion.", "#E0D8B8", [], [], 100, 0,
                    [(["WGFoodFishFillet"], 1), (["WGFoodPotatoSliced"], 1), (["WGFoodOnionSliced"], 1)]),
    "SeafoodBisque": ("seafood bisque", "A rich, rosy bisque of shellfish.", "#D06040", [], [], 110, 0,
                      [(["WGFoodShellfishMeat"], 2)]),
    "CheeseOnionSoup": ("cheese and onion soup", "Onion soup thick with melted cheese.", "#D8A848", [], [], 75, 0,
                        [(["FoodCheese", "FoodCheeseSlice"], 1), (["WGFoodOnionSliced"], 1)]),
    "CreamOfMushroom": ("cream of mushroom", "Mushrooms and garlic stewed into a velvety soup.", "#A89070", [], [], 80, 0,
                        [(["FoodMushroom"], 2), (["WGFoodGarlicClove"], 1)]),
    "FruitPorridge": ("fruit porridge", "Porridge stirred through with stewed berries.", "#C89090", [], [], 45, 0,
                      [(["WheatBushel", "OatBushel"], 1), (BERRIES, 1)]),
    "BorschtSoup": ("turnip and cabbage soup", "A ruddy soup of turnip and cabbage.", "#B05070", [], [], 80, 0,
                    [(["WGFoodTurnipSliced"], 1), (["WGFoodCabbageSliced"], 1)]),
}

# Cookware that does something: id -> (sheet, state, name, description, price, kind)
COOKWARE = {
    "WGCookPan": ("cookware/pan", "pan", "frying pan", "A well-seasoned frying pan. Set it on a lit hearth's hob and put food in to fry.", 40, "pan"),
    "WGCookPot": ("cookware/pot", "pote", "cooking pot", "A heavy iron pot. Fill it with water, set it on a lit hob to boil, then add ingredients for soup.", 40, "pot"),
    "WGCookPotCopper": ("cookware/pot", "pote_copper", "copper pot", "A polished copper pot for soups and stews.", 60, "pot"),
    "WGCookKettle": ("cookware/pot", "kettle", "kettle", "A kettle for the hob. Fill it with water, bring it to the boil, then drop in tea leaves or coffee beans, or spoon in tea powder, to brew.", 40, "kettle"),
    "WGCookBowl": ("cookware/bowl", "bowl", "wooden bowl", "A wooden bowl. Use it on a pot to ladle out soup.", 10, "bowl"),
    "WGCookTeapot": ("cookware/pot", "teapot_porcelain", "porcelain teapot", "A fine porcelain teapot. Fill it from the kettle to serve, then pour out the cups.", 80, "teapot"),
    "WGCookCup": ("cookware/cup", "steel", "steel cup", "A steel drinking cup. Fill it from the kettle or a teapot.", 15, "cup"),
    "WGCookPlatter": ("cookware/platter", "platter", "serving platter", "A wide platter for serving.", 25, "decor"),
    "WGCookPeppermill": ("cookware/peppermill", "peppermill", "pepper mill", "A wooden pepper mill.", 20, "decor"),
    "WGCookRollingPin": ("cookware/misc", "rolling_pin", "rolling pin", "A rolling pin for pastry.", 15, "decor"),
}


def sheet(dmi):
    im, desc = read_description(dmi)
    width, height, states = parse(desc)
    im = im.convert("RGBA")
    columns = im.width // width
    out = {}
    index = 0
    for state in states:
        tiles = []
        for frame in range(state["frames"]):
            i = index + frame * state["dirs"]
            x, y = (i % columns) * width, (i // columns) * height
            tiles.append(im.crop((x, y, x + width, y + height)))
        out.setdefault(state["name"], (tiles, state["delay"]))
        index += state["dirs"] * state["frames"]
    return out


def write_rsi(path, states, source):
    path.mkdir(parents=True, exist_ok=True)
    meta_states = []
    for name, (tiles, delay) in states.items():
        if len(tiles) == 1:
            tiles[0].save(path / f"{name}.png")
            meta_states.append({"name": name})
            continue
        strip = Image.new("RGBA", (32 * len(tiles), 32))
        for i, tile in enumerate(tiles):
            strip.paste(tile, (32 * i, 0))
        strip.save(path / f"{name}.png")
        delays = [round((d or 1) / 10, 3) for d in (delay or [1] * len(tiles))]
        meta_states.append({"name": name, "delays": [delays]})
    meta = {"version": 1, "license": "CC-BY-SA-3.0", "copyright": COPYRIGHT.format(file=source),
            "size": {"x": 32, "y": 32}, "states": meta_states}
    (path / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")


def food(fid, rsi, state, name, desc, nutriment, price, tags):
    return f"""- type: entity
  parent: FoodBase
  id: {fid}
  name: {name}
  description: {desc}
  components:
  - type: Sprite
    sprite: {rsi}
    state: {state}
  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: {nutriment + 5}
        reagents:
        - ReagentId: Nutriment
          Quantity: {nutriment}
        - ReagentId: Vitamin
          Quantity: 3
  - type: Tag
    tags: [ {", ".join(tags)} ]
  - type: StaticPrice
    price: {price}
"""


def main():
    sheets = {}

    def first(src, name):
        if src not in sheets:
            sheets[src] = sheet(SOURCE / FOOD / f"{src}.dmi")
        return [sheets[src][name][0][0]], None

    yml = ["# Generated by Tools/_WG/Cooking/port_paragon_kitchen.py from the Paragon Moon project (art CC BY-SA 3.0). Re-run it; don't edit.", ""]
    ftl = ["# Generated by Tools/_WG/Cooking/port_paragon_kitchen.py.", ""]

    dish_states = {}
    for did, (src, st, name, desc, nutriment, price, fine) in DISHES.items():
        dish_states[did.lower()] = first(src, st)
        entry = food(did, "_WG/Cooking/dishes.rsi", did.lower(), name, desc, nutriment, price,
                     ["WGFarmGood", "WGFineFood" if fine else "WGHomeCooking"])
        if did in MEALS:
            entry += meal_component(*MEALS[did])
        yml.append(entry)

    raw_states = {}
    for rid, (src, st, name, ingredients, result, method, seconds) in RAW.items():
        raw_states[rid.lower()] = first(src, st)
        how = "oven" if method == "Bake" else "pan"
        yml.append(food(rid, "_WG/Cooking/raw.rsi", rid.lower(), name, f"Ready for the {how}.", 4, 20, ["WGRawDish"]))
        lines = "".join(
            f"  - ids: [ {', '.join(ids)} ]\n    count: {count}\n" if not tag else f"  - tag: {tag}\n    count: {count}\n"
            for ids, tag, count in ingredients)
        yml.append(f"- type: wgPrepRecipe\n  id: WGPrep{rid[5:]}\n  result: {rid}\n  ingredients:\n{lines}")
        yml.append(f"- type: wgCookRecipe\n  id: WGCook{rid[5:]}\n  method: {method}\n  inputs: [ {rid} ]\n  result: {result}\n  seconds: {seconds}\n")
        # The same raw dish cooks in the kitchen machines too, so one recipe book serves every kitchen.
        machine = "Oven" if method == "Bake" else "Microwave"
        yml.append(f"- type: microwaveMealRecipe\n  id: WGRecipe{rid[5:]}\n  name: {DISHES[result][2]} recipe\n  result: {result}\n  time: {max(5, seconds // 3)}\n  group: Breads\n  recipeType: {machine}\n  solids:\n    {rid}: 1\n")

    for i, (method, inputs, result, seconds) in enumerate(SIMPLE):
        yml.append(f"- type: wgCookRecipe\n  id: WGCookSimple{i:02d}\n  method: {method}\n  inputs: [ {', '.join(inputs)} ]\n  result: {result}\n  seconds: {seconds}\n")

    slice_states = {}
    for sid, (src, st, name, sources, count) in SLICES.items():
        if src.startswith("@"):
            entry = food(sid, src[1:], st, name, "Chopped and ready for the pot or the pan.", 3, 8, ["WGChopped"])
            yml.append(entry.replace(f"    state: {st}\n", f"    state: {st}\n    scale: 0.75, 0.75\n", 1))
        else:
            slice_states[sid.lower()] = first(src, st)
            yml.append(food(sid, "_WG/Cooking/raw.rsi", sid.lower(), name, "Chopped and ready for the pot or the pan.", 4, 10, ["WGChopped"]))
        yml.append(f"- type: wgChopRecipe\n  id: WGChop{sid[6:]}\n  inputs: [ {', '.join(sources)} ]\n  result: {sid}\n  count: {count}\n")

    for sid, (name, desc, colour, inputs, tags, seconds, order, combine) in STEWS.items():
        key = sid.lower()
        yml.append(f"""- type: reagent
  id: WGSoup{sid}
  name: reagent-name-wg-soup-{key}
  desc: reagent-desc-wg-soup-{key}
  group: Foods
  physicalDesc: reagent-physical-desc-wg-soup
  flavor: savory
  color: "{colour}"
  metabolisms:
    Food:
      effects:
      - !type:SatiateHunger
        factor: 2
      - !type:WGMealBuff
        traits: [ {', '.join(SOUP_TRAITS.get(sid, ["Hearty"]))} ]
        strength: {1.25 if combine else 1.0}
        minutes: 15
      - !type:WGMendOrgans
        amount: {0.12 if combine else 0.08}
  pricePerUnit: 3
""")
        tag_line = f"  tags: [ {', '.join(tags)} ]\n" if tags else ""
        input_line = f"  inputs: [ {', '.join(inputs)} ]\n" if inputs else ""
        combine_lines = ("  combine:\n" + "".join(f"  - ids: [ {', '.join(ids)} ]\n    count: {count}\n" for ids, count in combine)) if combine else ""
        yml.append(f"- type: wgStewRecipe\n  id: WGStew{sid}\n{input_line}{tag_line}{combine_lines}  soup: WGSoup{sid}\n  seconds: {seconds}\n  order: {order}\n")
        ftl.append(f"reagent-name-wg-soup-{key} = {name}\nreagent-desc-wg-soup-{key} = {desc}")
    ftl.append("reagent-physical-desc-wg-soup = steaming")

    cook_states = {}
    for cid, (src, st, name, desc, price, kind) in COOKWARE.items():
        cook_states[cid.lower()] = first(src, st)
        extra = ""
        if kind == "pan":
            extra = "  - type: WGPan\n  - type: ContainerContainer\n    containers:\n      wg-pan: !type:Container\n"
        elif kind in ("pot", "kettle"):
            volume = 240 if kind == "pot" else 120
            extra = f"""  - type: WGPot
  - type: SolutionContainerManager
    solutions:
      pot:
        maxVol: {volume}
  - type: RefillableSolution
    solution: pot
  - type: DrainableSolution
    solution: pot
  - type: ExaminableSolution
    solution: pot
  - type: SolutionTransfer
    canChangeTransferAmount: true
  - type: Appearance
  - type: SolutionContainerVisuals
    maxFillLevels: 2
    fillBaseName: pot-fill
    solutionName: pot
"""
            if kind == "kettle":
                extra = extra.replace("  - type: Appearance\n  - type: SolutionContainerVisuals\n    maxFillLevels: 2\n    fillBaseName: pot-fill\n    solutionName: pot\n", "")
                # Only a kettle brews.
                extra = extra.replace("  - type: WGPot\n", "  - type: WGPot\n    kettle: true\n")
        elif kind == "teapot":
            extra = """  - type: SolutionContainerManager
    solutions:
      drink:
        maxVol: 120
  - type: RefillableSolution
    solution: drink
  - type: DrainableSolution
    solution: drink
  - type: ExaminableSolution
    solution: drink
  - type: SolutionTransfer
    transferAmount: 30
    canChangeTransferAmount: true
"""
        elif kind == "cup":
            extra = """  - type: SolutionContainerManager
    solutions:
      drink:
        maxVol: 30
  - type: RefillableSolution
    solution: drink
  - type: DrainableSolution
    solution: drink
  - type: ExaminableSolution
    solution: drink
  - type: SolutionTransfer
    transferAmount: 30
  - type: Drink
    solution: drink
"""
        elif kind == "bowl":
            extra = """  - type: SolutionContainerManager
    solutions:
      drink:
        maxVol: 30
  - type: RefillableSolution
    solution: drink
  - type: DrainableSolution
    solution: drink
  - type: ExaminableSolution
    solution: drink
  - type: SolutionTransfer
    transferAmount: 30
  - type: Drink
    solution: drink
  - type: WGServing
  - type: Appearance
  - type: SolutionContainerVisuals
    maxFillLevels: 2
    fillBaseName: bowl-fill
    solutionName: drink
"""
        if kind in ("kettle", "teapot", "cup"):
            extra += "  - type: GuideHelp\n    guides:\n    - WGBrewing\n"
        size = "Normal" if kind in ("pan", "pot") else "Small"
        fill = {"pot": "pot-fill", "bowl": "bowl-fill"}.get(kind)
        sprite = f"""    layers:
    - state: {cid.lower()}
    - state: {fill}1
      map: [ "enum.SolutionContainerLayers.Fill" ]
      visible: false""" if fill else f"    state: {cid.lower()}"
        yml.append(f"""- type: entity
  parent: BaseItem
  id: {cid}
  name: {name}
  description: {desc}
  components:
  - type: Sprite
    sprite: _WG/Cooking/cookware.rsi
{sprite}
  - type: Item
    size: {size}
  - type: StaticPrice
    price: {price}
{extra}""")

    light = sheet(SOURCE / "icons/roguetown/misc/lighting.dmi")
    hob_pan = first("cookware/pan", "pan")
    hob_pot = first("cookware/pot", "pote")
    write_rsi(TEXTURES / "hearth_stove.rsi",
              {"oven": light["oven0"], "oven-lit": light["oven1"], "hob-pan": hob_pan, "hob-pot": hob_pot},
              "icons/roguetown/misc/lighting.dmi and modular/Neu_Food/icons/cookware")
    board = first("cookware/platter", "platter_copper")
    write_rsi(TEXTURES / "prep_board.rsi", {"icon": board}, f"{FOOD}/cookware/platter.dmi")
    write_rsi(TEXTURES / "dishes.rsi", dish_states, f"{FOOD}/cooked/*.dmi")
    write_rsi(TEXTURES / "raw.rsi", {**raw_states, **slice_states}, f"{FOOD}/raw/*.dmi and cooked/*.dmi")
    bowls = sheet(SOURCE / FOOD / "cookware/bowl.dmi")
    pots = sheet(SOURCE / FOOD / "cookware/pot.dmi")
    cook_states["bowl-fill1"] = ([bowls["bowl_half"][0][0]], None)
    cook_states["bowl-fill2"] = ([bowls["bowl_full"][0][0]], None)
    cook_states["pot-fill1"] = ([pots["pote_half"][0][0]], None)
    cook_states["pot-fill2"] = ([pots["pote_full"][0][0]], None)
    write_rsi(TEXTURES / "cookware.rsi", cook_states, f"{FOOD}/cookware/*.dmi")

    yml.append("""- type: entity
  parent: BaseStructure
  id: WGHearthStove
  name: hearth stove
  description: A brick oven with a hob on top that burns wood like a fireplace. Feed it planks or logs, light it with a flame, then set a pan or a pot on the hob and put dishes in the oven. Keep an eye on them so they don't burn.
  components:
  - type: Construction
    graph: WGHearthStoveGraph
    node: built
  - type: Sprite
    sprite: _WG/Cooking/hearth_stove.rsi
    layers:
    - state: oven
    - state: oven-lit
      map: [ "lit" ]
      shader: unshaded
      visible: false
    - state: hob-pan
      map: [ "hob" ]
      offset: 0,0.35
      scale: 0.7,0.7
      visible: false
  - type: Appearance
  - type: GenericVisualizer
    visuals:
      enum.WGHobVisuals.Lit:
        lit:
          True: { visible: true }
          False: { visible: false }
      enum.WGHobVisuals.Cookware:
        hob:
          None: { visible: false }
          Pan: { visible: true, state: hob-pan }
          Pot: { visible: true, state: hob-pot }
  - type: WGBonfireFuel
    fuel: 0
    lit: false
  - type: PointLight
    enabled: false
    radius: 2
    energy: 1.5
    color: "#FF6F00"
  - type: AmbientSound
    enabled: false
    volume: -8
    range: 4
    sound:
      path: /Audio/Ambience/Objects/fireplace.ogg
  - type: Anchorable
  - type: WGHearth
  - type: UserInterface
    interfaces:
      enum.WGStirUiKey.Key:
        type: WGStirBoundUserInterface
  - type: ContainerContainer
    containers:
      wg-hob: !type:ContainerSlot
      wg-oven: !type:Container
  - type: StaticPrice
    price: 900
  - type: GuideHelp
    guides:
    - WGCooking
    - FoodRecipes

- type: entity
  parent: BaseStructureDynamic
  id: WGPrepBoard
  name: prep board
  description: Lay ingredients on it, then open it to chop them and put them together into a dish for the oven or the pan.
  components:
  - type: Construction
    graph: WGPrepBoardGraph
    node: built
  - type: Sprite
    sprite: _WG/Cooking/prep_board.rsi
    state: icon
  - type: Fixtures
    fixtures: {}
  - type: Physics
    canCollide: false
  - type: WGPrepBoard
  - type: ContainerContainer
    containers:
      wg-board: !type:Container
  - type: ActivatableUI
    key: enum.WGPrepBoardUiKey.Key
    requireActiveHand: false
  - type: UserInterface
    interfaces:
      enum.WGPrepBoardUiKey.Key:
        type: WGPrepBoardBoundUserInterface
  - type: StaticPrice
    price: 30

- type: entity
  parent: FoodBase
  id: WGFoodBurnedMess
  name: burned mess
  description: Whatever it was, it stayed on the heat too long.
  components:
  - type: Sprite
    sprite: _WG/Cooking/props.rsi
    state: burned
  - type: SolutionContainerManager
    solutions:
      food:
        maxVol: 5
        reagents:
        - ReagentId: Carbon
          Quantity: 5
  - type: StaticPrice
    price: 0

- type: Tag
  id: WGFineFood

- type: Tag
  id: WGHomeCooking

- type: Tag
  id: WGRawDish
""")
    ftl.append("")

    PROTOTYPES.mkdir(parents=True, exist_ok=True)
    LOCALE.mkdir(parents=True, exist_ok=True)
    (PROTOTYPES / "kitchen.yml").write_text("\n".join(yml), encoding="utf-8", newline="\n")
    (LOCALE / "kitchen.ftl").write_text("\n".join(ftl), encoding="utf-8", newline="\n")
    print(f"{len(DISHES)} dishes, {len(RAW)} raw, {len(STEWS)} stews, {len(COOKWARE)} cookware")


if __name__ == "__main__":
    main()
