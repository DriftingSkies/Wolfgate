"""Builds the hideout module prefabs from the layouts below, with a preview sheet for review.

Each prefab is a small grid a survey stake lays onto an outpost: its bottom row is the front, and the bottom middle tile
lands where the owner points. Rows are written top (north) to bottom (south).

Run from the repo root: python Tools/_WF/Hideouts/build_prefabs.py
Writes Resources/SharedMaps/_WF/Hideouts/<name>.yml and Docs/_WF/Hideouts/prefabs.png.
"""
import math
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'PlanetPois'))
from map_author import PoiMap  # noqa: E402

OUT = 'Resources/SharedMaps/_WF/Hideouts'
SHEET = 'Docs/_WF/Hideouts/prefabs.png'

FLOOR, WHITE, PLATE, BOLTS = 'WFFloorColonyGrey', 'WFFloorColonyWhite', 'Plating', 'WFFloorColonyGreyBolts'
WALL, WINDOW, DOOR, AIRLOCK, SHUTTERS = 'WFPrefabWallFixed', 'WFPrefabWindow', 'WFPrefabDoor', 'WFPrefabAirlock', 'WFPrefabShutters'
LIGHT, APC = 'PoweredSmallLight', 'APCBasic'

# Structure every prefab shares: tile, roofed, entities, and whether LV cable runs under it.
BASE = {
    '#': (PLATE, True, [WALL], False),
    'W': (PLATE, True, [WINDOW], False),
    'D': (FLOOR, True, [DOOR], True),
    'A': (FLOOR, True, [AIRLOCK], True),
    'S': (FLOOR, True, [SHUTTERS], True),
    '.': (FLOOR, True, [], True),
    ',': (WHITE, True, [], True),
    '_': (PLATE, False, [], False),
    '=': (BOLTS, False, [], False),
}

# Where a wall-mounted thing faces, by the side its wall is on: south is angle 0, counter-clockwise from there.
FACING = {(0, 1): 0.0, (1, 0): -math.pi / 2, (0, -1): math.pi, (-1, 0): math.pi / 2}


class Prefab:
    """One layout: its rows, what its own letters stand for, and where the MV feed runs."""

    def __init__(self, name, title, rows, legend=None, mv=(), floor=FLOOR):
        self.name, self.title = name, title
        self.rows = [row for row in rows.strip('\n').split('\n')]
        self.width = max(len(row) for row in self.rows)
        self.height = len(self.rows)
        self.legend = legend or {}
        self.mv = set(mv)
        self.floor = floor

    def cells(self):
        for row_index, row in enumerate(self.rows):
            for x, char in enumerate(row.ljust(self.width)):
                if char != ' ':
                    yield x, self.height - 1 - row_index, char

    def spec(self, char):
        """(tile, roofed, anchored entities, loose entities, lv cable, mv cable) for a letter."""
        if char in BASE:
            tile, roofed, entities, lv = BASE[char]
            return tile, roofed, entities, [], lv, False
        entry = self.legend[char]
        return (entry.get('tile', self.floor), entry.get('roof', True), entry.get('ents', []), entry.get('loose', []),
                entry.get('lv', entry.get('roof', True)), entry.get('mv', False))

    def wall_side(self, grid, x, y):
        for (dx, dy), angle in FACING.items():
            if grid.get((x + dx, y + dy)) in ('#', 'W'):
                return angle
        return 0.0

    def build(self):
        poi = PoiMap(self.name, self.width, self.height, 'Any', self.title, ground='Space')
        grid = {(x, y): char for x, y, char in self.cells()}
        for (x, y), char in grid.items():
            tile, roofed, entities, loose, lv, mv = self.spec(char)
            poi.tiles[x, y] = tile
            if roofed:
                poi.roofs.add((x, y))
            for proto in entities:
                if proto in (LIGHT, APC):
                    angle = self.wall_side(grid, x, y)
                    poi.entity(proto, x, y, rotation=f'{angle} rad' if angle else 0)
                else:
                    poi.entity(proto, x, y)
            for proto in loose:
                poi.entity(proto, x, y, anchored=False)
            if lv:
                poi.entity('CableApcExtension', x, y)
            if mv or (x, y) in self.mv or APC in entities:
                poi.entity('CableMV', x, y)
        return poi

    def run(self, x1, y1, x2, y2):
        """An MV run between two cells, cardinal, in layout coordinates with y counted from the bottom row."""
        for x in range(min(x1, x2), max(x1, x2) + 1):
            for y in range(min(y1, y2), max(y1, y2) + 1):
                self.mv.add((x, y))
        return self


def furniture(*ents, **extra):
    return {'ents': list(ents), **extra}


PREFABS = [
    # The faction hangar: an open apron 21 tiles square, as wide as the pad goes, with the beacon in its middle, a
    # floodlight at each corner of the ring round it, and two cranes and the refit console along the back.
    Prefab('hangar', 'Hangar apron', '''
f=====================f
=L=========C=========R=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=__________H__________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
=_____________________=
f=====================f
''', {
        'H': {'tile': PLATE, 'roof': False, 'ents': ['WFHideoutHangarPad'], 'lv': False},
        'L': {'tile': BOLTS, 'roof': False, 'ents': ['WFHideoutShipwrightCrane'], 'lv': False},
        'R': {'tile': BOLTS, 'roof': False, 'ents': ['WFHideoutShipwrightCraneMirrored'], 'lv': False},
        'C': {'tile': BOLTS, 'roof': False, 'ents': ['WFHideoutHangarConsole'], 'lv': False},
        'f': {'tile': BOLTS, 'roof': False, 'ents': ['WFOutpostFloodlight'], 'lv': False},
    }),

    # The shipwright bay: a 25-tile apron with its beacon in the middle, two cranes and the console along the back.
    # The cranes' arms run down over the apron; together they reach all of it.
    Prefab('shipwright_bay', 'Shipwright bay', '''
f=========================f
=L===========C===========R=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=____________B____________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
=_________________________=
f_________________________f
''', {
        'B': {'tile': PLATE, 'roof': False, 'ents': ['WFHideoutShipwrightPad'], 'lv': False},
        'L': {'tile': BOLTS, 'roof': False, 'ents': ['WFHideoutShipwrightCrane'], 'lv': False},
        'R': {'tile': BOLTS, 'roof': False, 'ents': ['WFHideoutShipwrightCraneMirrored'], 'lv': False},
        'C': {'tile': BOLTS, 'roof': False, 'ents': ['WFHideoutShipwrightConsole'], 'lv': False},
        'f': {'tile': BOLTS, 'roof': False, 'ents': ['WFOutpostFloodlight'], 'lv': False},
    }),

    # Quarters: a bunkhouse with three beds and a cryopod.
    Prefab('quarters', 'Quarters', '''
####W####
#b.b.b.c#
#.......#
Wt.....lW
#h.....P#
#.....r.#
####D####
''', {
        'b': furniture('Bed', 'BedsheetBrown'),
        'c': furniture('WFOutpostCryopod'),
        't': furniture('TableWood'),
        'h': furniture('ChairWood'),
        'l': furniture(LIGHT),
        'P': furniture(APC),
        'r': furniture('Dresser'),
    }).run(7, 0, 7, 2).run(4, 0, 7, 0),

    # Workshop: a fabricator, a charger and a recycler behind a pair of shutters.
    Prefab('workshop', 'Workshop', '''
#########
#F.t.t.R#
#.......#
Wl.....PW
#k.....m#
#.......#
###SSS###
''', {
        'F': furniture('WFOutpostFabricator'),
        'R': furniture('WFOutpostRecycler'),
        't': furniture('TableReinforced'),
        'k': furniture('Rack'),
        'm': furniture('WFOutpostMultiCharger'),
        'l': furniture(LIGHT),
        'P': furniture(APC),
    }).run(7, 0, 7, 3).run(4, 0, 7, 0),

    # Powerhouse: batteries indoors, two solar strings and a wind turbine in the yard behind.
    Prefab('powerhouse', 'Powerhouse', '''
=s=s=T=
#######
#B.L.B#
#l...P#
###D###
''', {
        's': {'tile': BOLTS, 'roof': False, 'ents': ['WFOutpostSolarString'], 'lv': False, 'mv': True},
        'T': {'tile': BOLTS, 'roof': False, 'ents': ['WFOutpostWindTurbine'], 'lv': False, 'mv': True},
        'B': furniture('WFOutpostBatterySmall', mv=True),
        'L': furniture('WFOutpostBatteryLarge', mv=True),
        'l': furniture(LIGHT),
        'P': furniture(APC),
    }).run(1, 4, 5, 4).run(1, 2, 5, 2).run(5, 1, 5, 4).run(3, 0, 3, 1).run(3, 1, 5, 1),

    # Medbay: two medical beds, a stasis bed and a stocked locker.
    Prefab('medbay', 'Medbay', '''
###W###
#m,m,k#
#,,,,,#
Wl,,,PW
#s,,t,#
###D###
''', {
        'm': furniture('MedicalBed', tile=WHITE),
        's': furniture('StasisBed', tile=WHITE),
        'k': furniture('LockerMedicineFilled', tile=WHITE),
        't': {'tile': WHITE, 'ents': ['TableReinforced'], 'loose': ['MedkitFilled']},
        'l': furniture(LIGHT, tile=WHITE),
        'P': furniture(APC, tile=WHITE),
    }, floor=WHITE).run(5, 0, 5, 2).run(3, 0, 5, 0),

    # Vault: a strongroom behind an airlock with deposit boxes, a deposit terminal and crates.
    Prefab('vault', 'Vault', '''
#######
#x.d.x#
#.....#
#l...P#
#c...c#
###A###
''', {
        'x': furniture('SafetyDepositBoxLarge'),
        'd': furniture('WFOutpostDepositTerminal'),
        'c': furniture('CrateSecure'),
        'l': furniture(LIGHT),
        'P': furniture(APC),
    }).run(5, 0, 5, 2).run(3, 0, 5, 0),

    # Company wing: a hall with a meeting table, three cryopods, a broadcast console and a telecom relay.
    Prefab('company_wing', 'Company wing', '''
######W###W######
#c.c.c#b...r#k.k#
#.....#.....#...#
#.....D.ttt.D...#
Wl....#.ttt.#..PW
#.....#hhhhh#...#
#.....#.....#...#
######W#AAA#W####
''', {
        'c': furniture('WFOutpostCryopod'),
        'b': furniture('WFOutpostBroadcastConsole'),
        'r': furniture('WFOutpostTelecomRelay'),
        't': furniture('TableReinforced'),
        'h': furniture('ChairOfficeDark'),
        'k': furniture('Rack'),
        'l': furniture(LIGHT),
        'P': furniture(APC),
    }).run(15, 0, 15, 3).run(8, 0, 15, 0),
]

COLOURS = {
    FLOOR: (118, 122, 128), WHITE: (196, 200, 204), PLATE: (72, 74, 80), BOLTS: (96, 100, 108),
    WALL: (38, 40, 46), WINDOW: (110, 170, 200), DOOR: (150, 110, 70), AIRLOCK: (190, 130, 60), SHUTTERS: (170, 150, 90),
}


def known_ids():
    ids = set()
    for root, _, files in os.walk('Resources/Prototypes'):
        for name in files:
            if name.endswith('.yml'):
                with open(os.path.join(root, name), encoding='utf-8', errors='ignore') as handle:
                    ids.update(re.findall(r'^  id: (\S+)', handle.read(), re.M))
    return ids


def draw(prefab, poi, scale=28):
    from PIL import Image, ImageDraw
    image = Image.new('RGB', (prefab.width * scale, prefab.height * scale + 22), (24, 26, 30))
    pen = ImageDraw.Draw(image)
    pen.text((4, 4), f'{prefab.title}  {prefab.width}x{prefab.height}  {len(poi.entities)} objects', fill=(230, 230, 230))
    for (x, y), tile in poi.tiles.items():
        if tile == 'Space':
            continue
        left, top = x * scale, (prefab.height - 1 - y) * scale + 22
        pen.rectangle([left, top, left + scale - 1, top + scale - 1], fill=COLOURS.get(tile, (90, 90, 90)), outline=(50, 52, 58))
        if (x, y) not in poi.roofs:
            pen.line([left + 3, top + scale - 4, left + scale - 4, top + 3], fill=(140, 150, 160))
    cables = {'CableApcExtension': (90, 200, 90), 'CableMV': (230, 200, 60)}
    for proto, x, y, _, anchored, _ in poi.entities:
        left, top = x * scale, (prefab.height - 1 - y) * scale + 22
        if proto in cables:
            offset = 4 if proto == 'CableMV' else 9
            pen.rectangle([left + offset, top + scale - 6, left + offset + 3, top + scale - 3], fill=cables[proto])
        elif proto in COLOURS:
            pen.rectangle([left + 1, top + 1, left + scale - 2, top + scale - 2], fill=COLOURS[proto])
        else:
            short = re.sub(r'^(WFHideoutShipwright|WFHideoutHangar|WFOutpost|WFHideout|WF)', '', proto)[:4] or proto[:4]
            pen.rectangle([left + 3, top + 3, left + scale - 4, top + scale - 10],
                          fill=(60, 110, 170) if anchored else (170, 90, 150))
            pen.text((left + 3, top + 5), short, fill=(255, 255, 255))
    return image


def main():
    ids = known_ids()
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(os.path.dirname(SHEET), exist_ok=True)
    images = []
    for prefab in PREFABS:
        poi = prefab.build()
        missing = sorted({entity[0] for entity in poi.entities} - ids)
        if missing:
            raise SystemExit(f'{prefab.name}: unknown prototypes {missing}')
        with open(os.path.join(OUT, prefab.name + '.yml'), 'w', encoding='utf-8', newline='\n') as handle:
            handle.write(poi.serialize())
        print(f'{prefab.name}: {prefab.width}x{prefab.height}, {len(poi.entities)} objects')
        images.append(draw(prefab, poi))

    from PIL import Image
    columns = 2
    rows = [images[i:i + columns] for i in range(0, len(images), columns)]
    width = max(sum(image.width for image in row) + 16 * (len(row) + 1) for row in rows)
    height = sum(max(image.height for image in row) + 16 for row in rows) + 16
    sheet = Image.new('RGB', (width, height), (16, 17, 20))
    top = 16
    for row in rows:
        left = 16
        for image in row:
            sheet.paste(image, (left, top))
            left += image.width + 16
        top += max(image.height for image in row) + 16
    sheet.save(SHEET)
    print('sheet:', SHEET)


if __name__ == '__main__':
    main()
