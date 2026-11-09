"""Writes the colony seat maps; --check fails when a written map is stale.

    python Tools/_WF/Colonies/seats.py            write the maps
    python Tools/_WF/Colonies/seats.py --check    fail when a written map differs

A seat is the ground a developed world's colony stands on, one map a tier, each the last with more built on:

    colony_field       Settled: a public landing pad and a shelter.
    colony_town_depot  Colony: the pad, a drop apron with a public trade hub, and the depot with the colony exchange
                       and a bunkroom.
    colony_town_hall   Charter and Dominion: the depot, and the governor's hall with the charter office.

Every seat faces south, with its road socket on the bottom edge. Content.Server/_WF/Colonies/README.md says how they
are placed; Resources/Prototypes/_WF/Colonies/seats.yml lists them with the footprints this prints.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools" / "_WF" / "PlanetPois"))
from map_author import PoiMap  # noqa: E402

OUT = ROOT / "Resources" / "Maps" / "_WF" / "Colonies"

GROUND = "RMCPlanetHybrisaCement"
FLOOR = "WFFloorColonyGrey"
BOLTS = "WFFloorColonyGreyBolts"
WALL = "WFPrefabWallFixed"
WINDOW = "WFPrefabWindow"
DOOR = "WFPrefabDoor"
SIDEWAYS = "1.5707963267948966 rad"
PAD = 13


def pad(m: PoiMap, x0: int, y0: int) -> None:
    """A public landing pad: an asphalt square edged in warning lines, floodlit at its corners."""
    m.rect(x0, y0, PAD, PAD, "RMCFloorAsphalt")
    for x in range(x0, x0 + PAD):
        m.decals.append(("WarnLineS", x, y0, 0))
        m.decals.append(("WarnLineN", x, y0 + PAD - 1, 0))
    for y in range(y0, y0 + PAD):
        m.decals.append(("WarnLineW", x0, y, 0))
        m.decals.append(("WarnLineE", x0 + PAD - 1, y, 0))
    for x in (x0 - 1, x0 + PAD):
        for y in (y0, y0 + PAD - 1):
            m.entity("RMCSmallFloodlight", x, y)
    m.entity("WFPoiPublicLandingPad", x0 + PAD // 2, y0 + PAD // 2)


def shell(m: PoiMap, x: int, y: int, w: int, h: int, doors: set, windows: set = frozenset()) -> None:
    """A roofed prefab building: floor, walls, and the doors and windows given as wall tiles."""
    m.rect(x, y, w, h, FLOOR)
    m.roof(x, y, w, h)
    for tx in range(x, x + w):
        for ty in range(y, y + h):
            if tx not in (x, x + w - 1) and ty not in (y, y + h - 1):
                continue
            if (tx, ty) in doors:
                m.entity(DOOR, tx, ty, SIDEWAYS if tx in (x, x + w - 1) else 0)
            else:
                m.entity(WINDOW if (tx, ty) in windows else WALL, tx, ty)


def bunks(m: PoiMap, tiles) -> None:
    for x, y in tiles:
        m.entity("Bed", x, y)
        m.entity("BedsheetBrown", x, y, anchored=False)


def field() -> PoiMap:
    width, height = 21, 25
    m = PoiMap("colony_field", width, height, "", "", ground=GROUND)
    pad(m, 4, 2)
    # The shelter: somewhere dry to wait for a lift.
    sx, sy, sw, sh = 6, 17, 9, 6
    shell(m, sx, sy, sw, sh, {(10, sy)}, {(8, sy), (12, sy)})
    for x in (7, 8, 12, 13):
        m.entity("ChairWood", x, sy + 4, 0)
    m.entity("TableWood", 7, sy + 2)
    m.entity("WaterCooler", 13, sy + 2)
    m.entity("AlwaysPoweredLightLED", 10, sy + 3)
    m.entity("WFColonySeatBeacon", 10, 16)
    m.socket(10, 0, "South")
    return m


def town(slug: str, hall: bool) -> PoiMap:
    width, height = 41, 43 if hall else 30
    m = PoiMap(slug, width, height, "", "", ground=GROUND)
    pad(m, 3, 2)

    # The drop apron: the public hub, with open sky round it for what comes down.
    m.rect(20, 3, 11, 11, BOLTS)
    m.entity("WFPoiPublicTradeHub", 25, 8)
    for x, y in ((19, 2), (31, 2), (19, 14), (31, 14)):
        m.entity("RMCSmallFloodlight", x, y)

    # The depot: the exchange counter on the left, the bunkroom on the right.
    dx, dy, dw, dh = 3, 17, 35, 11
    top = dy + dh - 1
    split = 24
    shell(m, dx, dy, dw, dh, {(10, dy), (11, dy), (30, dy)}, {(6, dy), (15, dy), (20, dy), (27, dy), (34, dy)})
    for y in range(dy + 1, top):
        m.entity(DOOR if y == dy + 5 else WALL, split, y, SIDEWAYS if y == dy + 5 else 0)
    m.entity("WFColonyDepotVendor", 6, top - 1)
    m.entity("WFColonyDepotChute", 9, top - 1)
    for x in range(12, 16):
        m.entity("TableReinforced", x, top - 3)
    m.entity("ComfyChair", 13, top - 2)
    for x in (18, 20, 22):
        m.entity("Rack", x, top - 1)
    for x, y in ((5, dy + 2), (18, dy + 3), (21, dy + 3)):
        m.entity("CrateGenericSteel", x, y)
    m.entity("WaterCooler", 4, dy + 5)
    for x in (8, 14, 20):
        m.entity("AlwaysPoweredLightLED", x, dy + 5)
    bunks(m, [(x, y) for x in (26, 29, 32, 35) for y in (top - 1, dy + 2) if (x, y) != (29, dy + 2)])
    m.entity("TableWood", 36, dy + 5)
    m.entity("ChairWood", 35, dy + 5)
    for x in (28, 34):
        m.entity("AlwaysPoweredLightLED", x, dy + 5)
    m.entity("WFColonySeatBeacon", 17, 15)

    if hall:
        # The governor's hall: the charter office at its head, a meeting table, and rooms for those who serve it.
        hx, hy, hw, hh = 8, 31, 25, 11
        htop = hy + hh - 1
        wing = 25
        shell(m, hx, hy, hw, hh, {(15, hy), (16, hy)}, {(11, hy), (20, hy), (28, hy), (12, htop), (19, htop)})
        for y in range(hy + 1, htop):
            m.entity(DOOR if y == hy + 5 else WALL, wing, y, SIDEWAYS if y == hy + 5 else 0)
        m.entity("WFColonyOfficeTerminal", 16, htop - 1)
        m.entity("ComfyChair", 15, htop - 1)
        for x in range(13, 19):
            m.entity("TableWood", x, hy + 4)
        for x in (13, 15, 17):
            m.entity("ChairWood", x, hy + 3)
            m.entity("ChairWood", x, hy + 5)
        for x in (9, 10, 22, 23):
            m.entity("BookshelfFilled", x, htop - 1)
        m.entity("PottedPlantRandom", 9, hy + 1)
        m.entity("PottedPlantRandom", 23, hy + 1)
        for x in (11, 16, 21):
            m.entity("AlwaysPoweredLightLED", x, hy + 7)
        bunks(m, [(x, y) for x in (27, 30) for y in (htop - 1, hy + 4, hy + 1)])
        m.entity("AlwaysPoweredLightLED", 29, hy + 6)
        m.entity("WFColonyHallBeacon", 20, hy - 2)

    m.socket(18, 0, "South")
    return m


def maps():
    return [field(), town("colony_town_depot", False), town("colony_town_hall", True)]


def entry(m: PoiMap) -> dict:
    ox, oy = m.width // 2, m.height // 2
    (sx, sy, _), = m.sockets
    return {"footprint": f"{-ox},{-oy},{m.width - ox},{m.height - oy}", "socket": f"{sx - ox},{sy - oy}",
            "beds": sum(1 for e in m.entities if e[0] == "Bed")}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    stale = []
    for m in maps():
        path = OUT / f"{m.slug}.yml"
        text = m.serialize()
        if args.check:
            if not path.exists() or path.read_text(encoding="utf-8") != text:
                stale.append(path)
            continue
        OUT.mkdir(parents=True, exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
        info = entry(m)
        print(f"{path.relative_to(ROOT)}: footprint {info['footprint']}, socket {info['socket']} South, {info['beds']} beds")
    if stale:
        print("Stale colony seat maps: " + ", ".join(str(p.relative_to(ROOT)) for p in stale))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
