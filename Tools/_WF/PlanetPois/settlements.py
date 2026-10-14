"""Authored roadside services, villages, work camps and abandoned hazard sites."""

from map_author import PoiMap


def _through(m, y, surface="RMCPlanetHybrisaAsphalt"):
    m.road(0, y, m.width - 1, y, surface=surface)
    m.socket(0, y, "West")
    m.socket(m.width - 1, y, "East")


def _walk(m, x, y, street):
    end = street - 4 if y < street else street + 4
    m.rect(x - 1, min(y, end), 3, abs(y - end) + 1, "FloorConcreteCrocus")


def _supplies(m, x, y, fuel=False):
    m.entity("WFPoiFuelTrader" if fuel else "WFPoiSupplyTrader", x, y, anchored=False)
    m.entity("WaterCooler", x + 5, y)
    if fuel:
        for dy, proto in enumerate(("FuelPlasma", "FuelUranium", "JerryCanWeldingFuel")):
            m.entity(proto, x + 9, y + 2 + dy, anchored=False)
    else:
        m.entity("CrateGenericSteel", x + 10, y)
        m.entity("ToolboxMechanicalFilled", x + 9, y + 2, anchored=False)


def _garden(m, x, y):
    m.rect(x, y, 8, 9, "FloorPlanetDirtCrocusNoEdge")
    for dx in (1, 5):
        for dy in (1, 4, 7):
            m.entity("hydroponicsTray", x + dx, y + dy)
    m.entity("WheatSeeds", x + 3, y + 2, anchored=False)
    m.entity("TomatoSeeds", x + 3, y + 5, anchored=False)
    m.entity("WaterTankFull", x + 3, y + 8)


def _village(ruined=False):
    width, height = (80, 56) if ruined else (64, 48)
    m = PoiMap("asclepiu_derelict_village" if ruined else "asclepiu_garden_village",
               width, height, "Asclepiu",
               "wf-poi-asclepiu-derelict-village" if ruined else "wf-poi-asclepiu-garden-village",
               inhabited=not ruined, ground="FloorPlanetGrassCrocusNoEdge")
    street = height // 2
    _through(m, street)
    if ruined:
        for x, kind in ((4, "market"), (29, "office"), (54, "warehouse")):
            m.building(x, 4, 22, 18, kind=kind, ruined=True, floor="FloorBrokenWood", door_side="north")
            m.building(x, 34, 22, 18, kind="home", ruined=True, floor="FloorBrokenWood")
            _walk(m, x + 11, 22, street)
            _walk(m, x + 11, 34, street)
            m.roof(x + 9, 42, 5, 5, covered=False)
            m.entity("N14DecorFloorBrickrubble", x + 4, 43)
        m.entity("ToolboxMechanicalFilled", 65, 13, anchored=False)
        m.entity("Medkit", 39, 44, anchored=False)
        m.entity("CrateGenericSteel", 68, 19)
        m.landmarks.extend([("Abandoned storefronts", 14, 12), ("Roofless homes", 39, 45), ("Salvage warehouse", 64, 13)])
    else:
        for x, kind in ((5, "market"), (37, "workshop")):
            m.building(x, 4, 21, 15, kind=kind, wall="WallWood", floor="FloorWood", door_side="north")
            _walk(m, x + 10, 19, street)
        for x in (5, 37):
            m.building(x, 31, 21, 13, kind="home", wall="WallWood", floor="FloorWood")
            _walk(m, x + 10, 31, street)
        _garden(m, 27, 34)
        _supplies(m, 8, 10)
        for x, y in ((3, 3), (61, 3), (3, 44), (61, 44)):
            m.entity("FloraTreeCrocusPine", x, y)
        m.entity("BenchParkMiddle", 29, 29)
        m.entity("LightPostSmall", 33, 29)
        m.landmarks.extend([("Village shop", 15, 11), ("Workshop", 47, 11), ("Homes", 15, 37), ("Communal food garden", 31, 38)])
    m.notes.extend(["Crocus grass, timber and concrete palette; two unobstructed five-wide road exits.",
                    "Derelict homes, roof gaps and recoverable supplies; no residents." if ruined else
                    "Occupied garden village with a working supply trader, furnished homes and plantable trays."])
    return m


def _camp(ruined=False):
    m = PoiMap("merak_abandoned_work_camp" if ruined else "merak_caravan_camp", 64, 48, "Merak",
               "wf-poi-merak-abandoned-work-camp" if ruined else "wf-poi-merak-caravan-camp",
               inhabited=not ruined, ground="FloorDesert")
    _through(m, 24)
    rooms = ((4, 4, 21, 15, "warehouse"), (38, 4, 21, 15, "workshop"),
             (4, 31, 21, 13, "home"), (38, 31, 21, 13, "market"))
    for x, y, w, h, kind in rooms:
        m.building(x, y, w, h, kind=kind, ruined=ruined,
                   wall="WallConcrete" if ruined else "WallSolid", floor="FloorConcreteMonoCrocus",
                   door_side="south" if y > 24 else "north")
        _walk(m, x + w // 2, y if y > 24 else y + h, 24)
    m.rect(27, 4, 8, 14, "FloorConcreteCrocus")
    for x, y in ((29, 7), (32, 12), (28, 16)):
        m.entity("CrateGenericSteel", x, y)
    m.entity("WaterTankFull", 29, 33)
    m.entity("N14BlueBarrel", 32, 38)
    if ruined:
        m.roof(42, 34, 9, 7, covered=False)
        m.roof(8, 7, 7, 6, covered=False)
        m.entity("N14DecorFloorBrickrubble", 11, 13)
        m.entity("N14ChairOfficeBlueBroken", 47, 39)
        m.entity("ToolboxMechanicalFilled", 50, 12, anchored=False)
    else:
        _supplies(m, 41, 35)
        m.entity("LightPostSmall", 28, 29)
        m.entity("LightPostSmall", 34, 19)
        m.entity("N14Bedroll", 8, 38)
    m.landmarks.extend([("Freight store", 14, 11), ("Repair shed", 48, 11), ("Crew bunkhouse", 14, 37),
                        ("Abandoned canteen" if ruined else "Caravan market", 48, 37)])
    m.notes.extend(["A clear through road separates shaded modular cabins from the freight apron.",
                    "Abandoned excavation camp: breached roofs, broken furnishings and salvage." if ruined else
                    "Occupied caravan stop with supply sales, water stock and sleeping space; no station links."])
    return m


def _trade_post(desert=False):
    planet = "Merak" if desert else "Asclepiu"
    slug = "merak_roadside_trader" if desert else "asclepiu_roadside_trader"
    m = PoiMap(slug, 40, 32, planet, f"wf-poi-{slug.replace('_', '-')}", connection="Roadside",
               ground="FloorDesert" if desert else "FloorPlanetGrassCrocusNoEdge")
    m.road(0, 11, 35, 11)
    m.socket(0, 11, "West")
    m.building(7, 18, 27, 11, kind="market", wall="WallSolid" if desert else "WallWood", floor="FloorWood")
    _walk(m, 20, 18, 11)
    _supplies(m, 10, 22)
    m.rect(24, 3, 11, 4, "FloorConcreteCrocus")
    m.entity("CrateGenericSteel", 27, 5)
    m.entity("CrateGenericSteel", 31, 5)
    m.entity("N14TableWoodCabledrum", 11, 5)
    m.entity("N14ChairWood", 13, 5)
    m.entity("LightPostSmall", 5, 15)
    m.landmarks.extend([("Supply counter", 16, 23), ("Loading apron", 29, 5), ("Roadside rest spot", 12, 5)])
    m.notes.extend(["Single clear driveway socket; the public counter and loading yard are reachable from the road.",
                    "Working finite-stock supply trader uses the existing Wolfgate commerce system."])
    return m


def _fuel_service(desert=False):
    planet = "Merak" if desert else "Asclepiu"
    slug = "merak_fuel_service" if desert else "asclepiu_fuel_service"
    m = PoiMap(slug, 48, 40, planet, f"wf-poi-{slug.replace('_', '-')}", connection="Roadside",
               ground="FloorDesert" if desert else "FloorPlanetGrassCrocusNoEdge")
    m.road(0, 14, 42, 14)
    m.socket(0, 14, "West")
    m.rect(14, 3, 28, 7, "FloorConcreteCrocus")
    m.building(7, 23, 32, 14, kind="workshop", wall="WallConcrete", floor="FloorConcreteMonoCrocus")
    _walk(m, 23, 23, 14)
    _supplies(m, 10, 28, fuel=True)
    m.entity("ToolboxMechanicalFilled", 29, 32, anchored=False)
    m.entity("WaterTankFull", 35, 28)
    m.entity("CrateGenericSteel", 37, 6)
    m.entity("N14BlackBarrelFull", 20, 6)
    m.entity("N14BlueBarrel", 24, 6)
    m.entity("ExtinguisherCabinetFilled", 37, 34)
    m.entity("LightPostSmall", 8, 18)
    if desert:
        m.roof(30, 30, 5, 5, covered=False)
        m.entity("N14DecorFloorBrickrubble", 34, 32)
    else:
        m.entity("PottedPlant0", 35, 34)
    m.landmarks.extend([("Fuel and tool counter", 16, 29), ("Workshop", 30, 29), ("Open loading bays", 25, 6)])
    m.notes.extend(["Operating roadside fuel sales and repair-supply stop; reagent cans and reactor fuel are real items.",
                    "The fuel trader sells existing catalogue stock; the stop offers no automatic docking/refuel service.",
                    "Merak's damaged service shed still operates beside an open loading yard." if desert else
                    "A maintained timber-and-concrete service stop with open-sky loading bays."])
    return m


def _hazard_site(planet, slug, title, ground, surface, kind):
    m = PoiMap(slug, 48, 40, planet, title, connection="Roadside", inhabited=False, ground=ground)
    m.road(0, 19, 42, 19, width=3, surface=surface, shoulder=ground, shoulder_width=1, markings=False)
    m.socket(0, 19, "West")
    m.building(6, 25, 22, 12, kind=kind, ruined=True, wall="WallConcrete", floor="FloorConcreteMonoCrocus")
    m.building(33, 25, 11, 12, kind="utility", ruined=True, wall="WallSolid", floor="FloorSteelDirty")
    _walk(m, 17, 25, 19)
    _walk(m, 38, 25, 19)
    m.rect(10, 4, 29, 9, "FloorConcreteCrocus")
    for x, y in ((13, 7), (18, 10), (33, 7)):
        m.entity("CrateGenericSteel", x, y)
    m.entity("ToolboxMechanicalFilled", 19, 30, anchored=False)
    m.entity("OxygenTankFilled", 37, 30, anchored=False)
    m.entity("N14Bedroll", 10, 31)
    m.entity("Medkit", 21, 33, anchored=False)
    m.entity("N14DecorFloorBrickrubble", 13, 11)
    m.roof(11, 30, 5, 5, covered=False)
    if planet == "Carcinoma":
        m.rect(30, 3, 12, 4, "FloorFlesh")
        m.entity("N14DecorFloorBrickrubble", 37, 6)
    elif planet == "Thrascias":
        m.rect(5, 3, 8, 7, "FloorSnow")
        m.entity("N14ChairOfficeBlueBroken", 25, 9)
    elif planet == "Aerumna":
        m.entity("N14BlackBarrelFull", 35, 10)
        m.entity("N14TableMetalGrate", 24, 7)
    else:
        m.entity("N14DecorBarrels", 25, 8)
    m.landmarks.extend([("Abandoned shelter", 17, 30), ("Utility annex", 38, 30), ("Salvage apron", 24, 9)])
    m.notes.extend(["Unoccupied exploration site; the roof is weather shelter, not a claim of breathable or temperature-safe atmosphere.",
                    "The public approach, roof gaps and salvage stock use existing Crocus-scale materials without station dependencies."])
    return m


def build_maps():
    return [
        _village(), _village(True), _camp(), _camp(True),
        _trade_post(), _trade_post(True), _fuel_service(), _fuel_service(True),
        _hazard_site("Fervidus", "fervidus_rim_shelter", "wf-poi-fervidus-rim-shelter", "FloorBasalt", "FloorDarkPavement", "warehouse"),
        _hazard_site("Thrascias", "thrascias_survey_shelter", "wf-poi-thrascias-survey-shelter", "FloorSnow", "FloorSnowDug", "office"),
        _hazard_site("Aerumna", "aerumna_salvage_yard", "wf-poi-aerumna-salvage-yard", "FloorChromite", "FloorChromite", "workshop"),
        _hazard_site("Carcinoma", "carcinoma_ruin_shelter", "wf-poi-carcinoma-ruin-shelter", "FloorFlesh", "FloorDirt", "clinic"),
    ]
