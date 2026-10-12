"""Crocus-scale city districts with inhabited blocks and abandoned quarters."""

from map_author import PoiMap


def _streets(m):
    m.road(0, 72, 287, 72)
    m.road(72, 4, 72, 143)
    m.road(216, 0, 216, 140)
    for y in (4, 36, 108, 140):
        m.road(4, y, 284, y, width=3)
    for x in (4, 144, 284):
        m.road(x, 4, x, 140, width=3)
    m.socket(0, 72, "West")
    m.socket(287, 72, "East")
    m.socket(72, 143, "North")
    m.socket(216, 0, "South")
    for x in range(16, 280, 24):
        for y in (68, 76):
            m.entity("LightPostSmall", x, y)
    for x in (68, 76, 212, 220):
        for y in (20, 52, 92, 124):
            m.entity("LightPostSmall", x, y)


def _forecourt(m, x, y, w, street):
    # Door-to-street access stays outside the walls and clear of furniture.
    m.rect(x + w // 2 - 1, street + 4, 3, y - street - 4, "FloorConcreteCrocus")


def _park(m, x, y, desert=False):
    m.rect(x, y, 21, 20, "FloorDesert" if desert else "FloorGrass")
    m.rect(x + 1, y + 8, 19, 3, "FloorConcreteCrocus")
    m.rect(x + 9, y + 1, 3, 18, "FloorConcreteCrocus")
    for dx, dy in ((3, 4), (17, 4), (3, 15), (17, 15)):
        m.entity("PottedPlant0" if desert else "FloraTreeCrocusPine", x + dx, y + dy)
    for dx in (5, 15):
        m.entity("BenchParkMiddle", x + dx, y + 7)
    m.entity("WaterCooler", x + 7, y + 12)
    m.entity("LightPostSmall", x + 13, y + 12)


def _trader(m, x, y, fuel=False):
    m.entity("WFPoiFuelTrader" if fuel else "WFPoiSupplyTrader", x + 5, y + 10, anchored=False)
    m.entity("WaterTankFull", x + 16, y + 15)
    m.entity("CrateGenericSteel", x + 16, y + 12)


def _city(asclepiu):
    desert = not asclepiu
    m = PoiMap(
        "asclepiu_city" if asclepiu else "merak_caravan_city",
        288, 144,
        "Asclepiu" if asclepiu else "Merak",
        "wf-poi-asclepiu-city" if asclepiu else "wf-poi-merak-caravan-city",
        ground="FloorGrass" if asclepiu else "FloorDesert",
    )
    _streets(m)
    rows = ((10, 4), (44, 36), (82, 72), (116, 108))
    columns = ((10, 40), (82, 112), (154, 184), (226, 256))
    kinds = (
        ("home", "home", "clinic", "office", "workshop", "warehouse", "warehouse", "utility"),
        ("cafe", "market", "office", "market", "home", "home", "workshop", "warehouse"),
        ("home", "home", "clinic", "office", "home", "home", "warehouse", "workshop"),
        ("home", "home", "utility", "workshop", "home", "home", "warehouse", "utility"),
    )
    for row, (y, street) in enumerate(rows):
        index = 0
        for column, xs in enumerate(columns):
            for x in xs:
                kind = kinds[row][index]
                index += 1
                # The occupied core borders an old industrial/residential quarter.
                ruined = column >= 2 and (row != 1 or column == 3)
                if (row, column, x == xs[0]) in ((1, 1, True), (2, 1, False)):
                    _park(m, x, y, desert)
                    _forecourt(m, x, y, 21, street)
                    continue
                wall = "WallSolid" if desert and not ruined else "WallConcrete"
                if kind == "home" and asclepiu and not ruined:
                    wall = "WallWood"
                floor = "FloorBrokenWood" if ruined else (
                    "FloorConcreteMonoCrocus" if desert or kind in ("workshop", "warehouse", "utility")
                    else "FloorWood"
                )
                m.building(x, y, 21, 20, kind=kind, ruined=ruined, wall=wall, floor=floor)
                _forecourt(m, x, y, 21, street)
                if ruined:
                    # Roof gaps and scattered salvage distinguish derelict blocks.
                    m.roof(x + 10, y + 10, 5, 5, covered=False)
                    m.entity("CrateGenericSteel", x + 16, y + 15)
                elif kind == "market":
                    _trader(m, x, y)
                elif kind == "clinic":
                    m.entity("Medkit", x + 16, y + 15, anchored=False)
                    m.entity("WaterCooler", x + 16, y + 12)
                elif kind == "workshop":
                    m.entity("CrateGenericSteel", x + 16, y + 15)
                elif kind == "utility":
                    m.entity("WaterTankFull", x + 16, y + 15)
                elif kind == "office":
                    m.entity("BookshelfFilled", x + 16, y + 15)
                if not ruined and not desert:
                    m.entity("PottedPlant0", x + 2, y + 17)
    # Freight aprons and roadside water stock remain away from every street socket.
    for x in (88, 118):
        m.entity("CrateGenericSteel", x, 40)
        m.entity("WaterTankFull", x + 3, 40)
    if asclepiu:
        for x in (26, 54, 98, 126):
            m.entity("FloraTreeCrocusPine", x, 79)
    else:
        for x in (24, 52, 96, 124):
            m.entity("CrateGenericSteel", x, 79)
    m.landmarks.extend([
        ("Occupied homes", 32, 22),
        ("Clinic", 92, 20),
        ("Market", 50, 54),
        ("Public garden" if asclepiu else "Caravan square", 92, 54),
        ("Old industrial quarter", 240, 124),
        ("Derelict residences", 178, 22),
    ])
    m.notes.extend([
        "288 by 144 tiles: rounded Crocus ground chunk envelope; Crocus's actual occupied tile bounds are 281 by 140.",
        "Four clear five-wide trunk exits, three-wide local loops, passive doors and selective building roofs.",
        "Occupied western service core and deliberately abandoned eastern blocks; no station or shuttle setup.",
        "Merak is a temporary modular caravan district beside quake-damaged old buildings." if desert else
        "Asclepiu combines inhabited housing, clinic and markets with an abandoned industrial quarter.",
    ])
    return m


def build_maps():
    return [_city(True), _city(False)]
