"""Builds the hideout module prefabs from the layouts below, with a preview sheet for review.

Each prefab is a small grid a survey stake lays onto an outpost: its bottom row is the front, and the bottom middle tile
lands where the owner points. Rows are written top (north) to bottom (south).

The layouts follow the planetary hospital: rooms with a floor each, windows along the outer walls, a porch out front,
and furniture, clutter and floor decals so a module looks lived in the moment it lands. A module has several layouts
to pick from on its stake; the first is the one the stake starts on, written to <name>.yml, the others to
<name>_<key>.yml, each listed under the level's `layouts` in modules.yml. Every layout of a module holds the same
machines.

They are drawn in the colony materials; a theme (Resources/Prototypes/_WF/Hideouts/themes.yml) swaps the walls,
windows, doors, the three indoor floors and the porch as a stake lays them, so use only FLOOR, ACCENT and WHITE
indoors and PORCH out front.

Nothing may stand in the way: the tile either side of a door is kept clear, every open floor tile has to be reachable
from the front door, and everything standing indoors needs an open tile beside it. The build stops otherwise.

An APC, a sign or a painting hangs on the wall beside the cell its letter is in (`mount`), facing into the room, as the
hospital's do; the wall carries the APC's cables. The build stops if any cable doesn't reach the APC or the MV feed
doesn't come out at the front door.

Run from the repo root: python Tools/_WF/Hideouts/build_prefabs.py
Writes Resources/SharedMaps/_WF/Hideouts/*.yml and Docs/_WF/Hideouts/prefabs.png.
"""
import math
import os
import re
import sys
import zlib

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'PlanetPois'))
from map_author import PoiMap  # noqa: E402

OUT = 'Resources/SharedMaps/_WF/Hideouts'
SHEET = 'Docs/_WF/Hideouts/prefabs.png'

# The floors a theme swaps: the main floor, the accent of living rooms, the clean floor of a medbay and the porch.
FLOOR, ACCENT, WHITE, PORCH = 'WFFloorColonyGrey', 'WFFloorColonyGreyTexture', 'WFFloorColonyWhite', 'WFFloorColonyWhiteBolts'
PLATE, BOLTS, GRASS = 'Plating', 'WFFloorColonyGreyBolts', 'FloorAstroGrass'
WALL, WINDOW, DOOR, AIRLOCK, SHUTTERS = 'WFPrefabWallFixed', 'WFPrefabWindow', 'WFPrefabDoor', 'WFPrefabAirlock', 'WFPrefabShutters'
LIGHT, APC = 'PoweredSmallLight', 'APCBasic'

# Structure every prefab shares: tile (None for the prefab's own floor), roofed, entities, and whether LV cable runs under it.
BASE = {
    '#': (PLATE, True, [WALL], False),
    'W': (PLATE, True, [WINDOW], False),
    'D': (None, True, [DOOR], True),
    'A': (None, True, [AIRLOCK], True),
    'S': (None, True, [SHUTTERS], True),
    '.': (FLOOR, True, [], True),
    ';': (ACCENT, True, [], True),
    ',': (WHITE, True, [], True),
    '_': (PLATE, False, [], False),
    '=': (BOLTS, False, [], False),
    '-': (PORCH, False, [], False),
}

# Where a light on a floor tile faces, by the side its wall is on: south is angle 0, counter-clockwise from there.
FACING = {(0, 1): 0.0, (1, 0): -math.pi / 2, (0, -1): math.pi, (-1, 0): math.pi / 2}

# The angle of something facing a compass direction.
ANGLE = {'S': 0.0, 'E': math.pi / 2, 'N': math.pi, 'W': -math.pi / 2}
TOWARD = {(0, -1): 'S', (1, 0): 'E', (0, 1): 'N', (-1, 0): 'W'}

# Hazard lines by the edge of the tile they are drawn on. The decals are named for other edges than they draw.
EDGE = {'N': 'WarnLineW', 'S': 'WarnLineN', 'W': 'WarnLineS', 'E': 'WarnLineE'}

# Wear scattered over indoor floors, and how many tiles in a hundred get some.
WEAR = ['DirtLight', 'DirtLight', 'DirtLight', 'DirtMedium']
WEAR_SHARE = 11

# Carpets lie under what stands on them and turn with nothing.
CARPETS = {'Carpet', 'CarpetBlue', 'CarpetGreen', 'CarpetOrange', 'CarpetPurple', 'CarpetBlack', 'CarpetSBlue'}


# What can be walked over or past: seats and curtains. Carpets and lights never count as standing on a tile.
SEATS = ('Chair', 'Stool', 'ComfyChair', 'Sofa', 'WoodenBench', 'ShiptestChairBench', 'HospitalCurtains')


class Prefab:
    """One layout of a module: its rows, what its own letters stand for, and where the MV feed runs."""

    def __init__(self, module, key, title, rows, legend=None, floor=FLOOR, wear=True):
        self.module, self.key, self.title = module, key, title
        self.name = module if key == 'a' else f'{module}_{key}'
        self.rows = [row for row in rows.strip('\n').split('\n')]
        self.width = max(len(row) for row in self.rows)
        self.height = len(self.rows)
        for row in self.rows:
            if row[0] != ' ' and len(row) != self.width:
                raise SystemExit(f'{self.name}: row "{row}" is {len(row)} wide, not {self.width}')
        self.legend = legend or {}
        clash = sorted(set(self.legend) & set(BASE))
        if clash:
            raise SystemExit(f'{self.name}: letters {clash} are structure letters')
        self.mv = set()
        self.floor = floor
        self.wear = wear
        self.decals = []
        self.grid = {(x, y): char for x, y, char in self.cells()}
        unknown = sorted({char for char in self.grid.values() if char not in BASE and char not in self.legend})
        if unknown:
            raise SystemExit(f'{self.name}: letters {unknown} have no legend entry')

    def cells(self):
        for row_index, row in enumerate(self.rows):
            for x, char in enumerate(row.ljust(self.width)):
                if char != ' ':
                    yield x, self.height - 1 - row_index, char

    def spec(self, char):
        """A letter's entry: tile, roof, ents, loose, lv, mv, decals, mount, face."""
        if char in BASE:
            tile, roofed, entities, lv = BASE[char]
            return {'tile': tile or self.floor, 'roof': roofed, 'ents': entities, 'lv': lv}
        entry = dict(self.legend[char])
        entry.setdefault('tile', self.floor)
        entry.setdefault('roof', True)
        entry.setdefault('lv', entry['roof'])
        return entry

    def wall_beside(self, x, y, kinds='#'):
        """The wall cell beside a cell and the side it is on, north first."""
        for (dx, dy) in FACING:
            if self.grid.get((x + dx, y + dy), ' ') in kinds:
                return (x + dx, y + dy), (dx, dy)
        raise SystemExit(f'{self.name}: nothing at {x},{y} to mount on')

    def apc(self):
        """The wall cell the APC hangs on, or None for a prefab with no APC."""
        for (x, y), char in self.grid.items():
            if char not in BASE and APC in self.legend[char].get('mount', []):
                return self.wall_beside(x, y)[0]
        return None

    def build(self):
        poi = PoiMap(self.name, self.width, self.height, 'Any', self.title, ground='Space')
        mount = self.apc()
        lv_cells, mv_cells = set(), set()
        for (x, y), char in sorted(self.grid.items()):
            entry = self.spec(char)
            entities = entry.get('ents', [])
            poi.tiles[x, y] = entry['tile']
            if entry['roof']:
                poi.roofs.add((x, y))
            face = entry.get('face')
            for proto in entities:
                if proto == LIGHT:
                    angle = FACING[self.wall_beside(x, y, '#W')[1]]
                elif face and proto not in CARPETS:
                    angle = ANGLE[face]
                else:
                    angle = 0.0
                poi.entity(proto, x, y, rotation=f'{angle} rad' if angle else 0)
            for proto in entry.get('mount', []):
                # On the wall beside the letter, facing back into the room.
                (wx, wy), (dx, dy) = self.wall_beside(x, y)
                angle = ANGLE[TOWARD[(-dx, -dy)]]
                poi.entity(proto, wx, wy, rotation=f'{angle} rad' if angle else 0)
            for proto in entry.get('loose', []):
                poi.entity(proto, x, y, anchored=False)
            if entry['lv'] or (x, y) == mount:
                poi.entity('CableApcExtension', x, y)
                lv_cells.add((x, y))
            if entry.get('mv') or (x, y) in self.mv or (x, y) == mount:
                poi.entity('CableMV', x, y)
                mv_cells.add((x, y))
            for decal in entry.get('decals', []):
                poi.decals.append((decal, x, y, '0 rad'))
            # Scuffs on bare indoor floor, the same ones every run.
            if self.wear and entry['roof'] and not entities and entry['tile'] in (FLOOR, ACCENT, WHITE) and char not in 'DAS':
                roll = zlib.crc32(f'{self.name}:{x}:{y}'.encode())
                if roll % 100 < WEAR_SHARE:
                    poi.decals.append((WEAR[(roll // 100) % len(WEAR)], x, y, '0 rad'))
        for decal, x, y in self.decals:
            poi.decals.append((decal, x, y, '0 rad'))
        if mount:
            self.check_wiring(mount, lv_cells, mv_cells)
        self.check_access()
        return poi

    def standing(self, cell):
        """What stands on a cell, carpets and lights left out."""
        return [proto for proto in self.spec(self.grid[cell]).get('ents', []) if proto not in CARPETS and proto != LIGHT]

    def blocked(self, cell):
        """Whether a cell can't be walked through: a wall or window, or something other than a seat standing on it."""
        char = self.grid[cell]
        if char in '#W':
            return True
        return char not in 'DAS' and any(not proto.startswith(SEATS) for proto in self.standing(cell))

    def check_access(self):
        """Doors are kept clear either side, all open floor is reachable from the front door, and furniture can be got at."""
        doors = [cell for cell, char in self.grid.items() if char in 'DAS']
        if not doors:
            return

        for (x, y) in doors:
            # A door is walked through across the wall it is set in.
            across = [(x, y + 1), (x, y - 1)] if self.grid.get((x - 1, y), ' ') in '#WDAS' else [(x + 1, y), (x - 1, y)]
            for cell in across:
                if cell in self.grid and (self.grid[cell] in '#W' or self.grid[cell] not in 'DAS' and self.standing(cell)):
                    raise SystemExit(f'{self.name}: the door at {x},{y} is blocked by {self.grid[cell]!r} at {cell}')

        open_cells = {cell for cell in self.grid if not self.blocked(cell)}
        reached = set()
        for door in self.front_doors():
            reached |= self.reach(door, open_cells)
        indoors = {cell for cell, char in self.grid.items() if self.spec(char)['roof'] and char not in '#W'}
        cut_off = sorted((indoors & open_cells) - reached)
        if cut_off:
            raise SystemExit(f'{self.name}: floor at {cut_off} cannot be reached from the front door')
        for (x, y) in sorted(indoors - open_cells):
            if not any(step in reached for step in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1))):
                raise SystemExit(f'{self.name}: {self.grid[(x, y)]!r} at {x},{y} has no open tile beside it')

    @staticmethod
    def reach(start, cells):
        """The cells of a cable run joined to `start`, side to side."""
        seen, todo = {start}, [start]
        while todo:
            x, y = todo.pop()
            for step in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if step in cells and step not in seen:
                    seen.add(step)
                    todo.append(step)
        return seen

    def front_doors(self):
        doors = [cell for cell, char in self.grid.items() if char in 'DAS']
        front = min(y for _, y in doors)
        return [cell for cell in doors if cell[1] == front]

    def check_wiring(self, mount, lv_cells, mv_cells):
        """Every LV cable has to join the APC, every MV cable too, and the MV has to come out at a front door."""
        loose = sorted(lv_cells - self.reach(mount, lv_cells))
        if loose:
            raise SystemExit(f'{self.name}: LV cable at {loose} does not reach the APC')
        fed = self.reach(mount, mv_cells)
        stray = sorted(mv_cells - fed)
        if stray:
            raise SystemExit(f'{self.name}: MV cable at {stray} does not reach the APC')
        if not any(cell in fed for cell in self.front_doors()):
            raise SystemExit(f'{self.name}: the MV feed does not come out at the front door')

    def run(self, x1, y1, x2, y2):
        """An MV run between two cells, cardinal, in layout coordinates with y counted from the bottom row."""
        for x in range(min(x1, x2), max(x1, x2) + 1):
            for y in range(min(y1, y2), max(y1, y2) + 1):
                self.mv.add((x, y))
        return self

    def feed(self):
        """Runs MV from the APC's wall straight down to the front door's row and along it to the door, for the owner to tie in."""
        apc = self.apc()
        door = min(self.front_doors(), key=lambda cell: abs(cell[0] - apc[0]))
        return self.run(apc[0], door[1], apc[0], apc[1]).run(door[0], door[1], apc[0], door[1])

    def border(self, x1, y1, x2, y2):
        """Hazard lines along the inside edge of a box of cells, in layout coordinates with y counted from the bottom row."""
        for x in range(x1, x2 + 1):
            self.decals.append((EDGE['S'], x, y1))
            self.decals.append((EDGE['N'], x, y2))
        for y in range(y1, y2 + 1):
            self.decals.append((EDGE['W'], x1, y))
            self.decals.append((EDGE['E'], x2, y))
        return self


def f(*ents, **extra):
    """Furniture on the prefab's own floor."""
    return {'ents': list(ents), **extra}


def a(*ents, **extra):
    """Furniture on the accent floor."""
    return {'tile': ACCENT, 'ents': list(ents), **extra}


def w(*ents, **extra):
    """Furniture on the clean floor."""
    return {'tile': WHITE, 'ents': list(ents), **extra}


def lawn(*decals, **extra):
    """Open grass, with the flowers or bushes growing on it."""
    return {'tile': GRASS, 'roof': False, 'lv': False, 'decals': list(decals), **extra}


def yard(*ents, **extra):
    """Something standing out of doors, on a pad of bolted plate."""
    return {'tile': BOLTS, 'roof': False, 'ents': list(ents), 'lv': False, **extra}


def porch(*ents, **extra):
    """Something standing on the porch."""
    return {'tile': PORCH, 'roof': False, 'ents': list(ents), 'lv': False, **extra}


# Letters every building shares.
COMMON = {
    'l': f(LIGHT),
    '^': a(LIGHT),
    'i': w(LIGHT),
    'g': lawn(),
    'P': {'mount': [APC]},
    'B': porch('WoodenBench'),
    'p': porch('PottedPlant2'),
}


def legend(**letters):
    return {**COMMON, **letters}


def apron(size, pad, console):
    """An open apron `size` tiles square inside a ring of bolted plate, with cranes, a console and a tool corner along the back."""
    inner = '_' * size
    middle = size // 2
    rows = ['f' + '=' * size + 'f', '=Lkx' + '=' * (middle - 3) + 'C' + '=' * (middle - 3) + 'xkR=']
    rows += ['=' + inner + '=' for _ in range(size)]
    rows[2 + middle] = '=' + '_' * middle + 'H' + '_' * middle + '='
    rows.append('f' + '=' * size + 'f')
    return '\n'.join(rows), {
        'H': {'tile': PLATE, 'roof': False, 'ents': [pad], 'lv': False, 'decals': ['Delivery']},
        'L': yard('WFHideoutShipwrightCrane'),
        'R': yard('WFHideoutShipwrightCraneMirrored'),
        'C': yard(console, decals=['Bot']),
        'f': yard('WFOutpostFloodlight'),
        'k': yard('Rack', loose=['ToolboxMechanicalFilled']),
        'x': yard('CrateGenericSteel', decals=['Bot']),
    }


HANGAR_ROWS, HANGAR_LEGEND = apron(21, 'WFHideoutHangarPad', 'WFHideoutHangarConsole')
BAY_ROWS, BAY_LEGEND = apron(25, 'WFHideoutShipwrightPad', 'WFHideoutShipwrightConsole')

BED = {'b': 'BedsheetBrown', 'g': 'BedsheetGreen', 'y': 'BedsheetBlue', 'r': 'BedsheetRed'}

PREFABS = [
    # The faction hangar: an open apron 21 tiles square, as wide as the pad goes, hazard lines round its edge, the
    # beacon in its middle, a floodlight at each corner, and the cranes, refit console and a tool corner along the back.
    Prefab('hangar', 'a', 'Hangar apron', HANGAR_ROWS, HANGAR_LEGEND, wear=False).border(1, 1, 21, 21),

    # The shipwright bay: the same on a 25-tile apron. The cranes' arms run down over the apron; together they reach all of it.
    Prefab('shipwright_bay', 'a', 'Shipwright bay', BAY_ROWS, BAY_LEGEND, wear=False).border(1, 1, 25, 25),

    # Quarters, bunkhouse: a bunk room with a cryopod, and a common room with a dining table, a reading corner and a
    # kitchenette.
    Prefab('quarters', 'a', 'Quarters: bunkhouse', '''
####W####W###
#b;r#k.ttt.o#
#;;^#..hhh..W
Wb;;D.......#
#;;;#~~~..nP#
#c;w#e~e.lm.#
#####W##D####
 -B----_---p
''', legend(
        b=a('Bed', 'BedsheetBrown'),
        r=a('Dresser', mount=['PaintingNightHawks']),
        w=a('WardrobeMixed'),
        c=a('WFOutpostCryopod'),
        k=f('Bookshelf'),
        t=f('TableWood', loose=['DrinkMugOne']),
        h=f('ChairWood', face='N'),
        o=f('PottedPlant1'),
        **{'~': a('CarpetBlue')},
        e=a('CarpetBlue', 'ComfyChair'),
        n=f('TableCounterWood', loose=['FoodBoxDonut']),
        m=f('TableCounterWood', loose=['KitchenMicrowave']),
    )).feed(),

    # Quarters, cabin: one bedroom and a living room round a fireplace, with a sofa on the rug and a kitchen corner.
    Prefab('quarters', 'b', 'Quarters: cabin', '''
####W####W###
#b;r#k.F..ho#
#g;^#.......W
W;;;D.~~~~nm#
#c;w#.zMs~.P#
#;;;#l.....t#
#####W##D####
 -B----_---p
''', legend(
        b=a('Bed', 'BedsheetBrown'),
        g=a('Bed', 'BedsheetGreen'),
        r=a('Dresser'),
        w=a('WardrobeMixed'),
        c=a('WFOutpostCryopod'),
        k=f('Bookshelf'),
        F=f('Fireplace'),
        h=f('ComfyChair'),
        o=f('PottedPlant5'),
        **{'~': a('CarpetOrange')},
        z=a('CarpetOrange', 'SofaEndRightBrown', face='N'),
        M=a('CarpetOrange', 'SofaMiddleBrown', face='N'),
        s=a('CarpetOrange', 'SofaEndLeftBrown', face='N'),
        n=f('TableCounterWood', loose=['DrinkMugOne']),
        m=f('TableCounterWood', loose=['KitchenMicrowave']),
        t=f('TableWood', loose=['BookRandom'], mount=['PaintingTheGreatWave']),
    )).feed(),

    # Quarters, dormitory: four bunks with lockers between along one wall, a rug with an armchair, and a mess table.
    Prefab('quarters', 'c', 'Quarters: dormitory', '''
###W###W###W#
#bLgLyLr.w.c#
#;;;;;;;...lW
W;;;;;;;.hh.#
#^~~~~~;.tt.#
#e~~~~~;.HHP#
#####W##D####
 -B----_---p
''', legend(
        **{letter: a('Bed', sheet) for letter, sheet in BED.items()},
        L=a('LockerSteel'),
        w=f('WardrobeMixed'),
        c=f('WFOutpostCryopod'),
        h=f('ChairWood'),
        H=f('ChairWood', face='N'),
        t=f('TableWood', loose=['DrinkMugOne']),
        **{'~': a('CarpetGreen')},
        e=a('CarpetGreen', 'ComfyChair'),
    )).feed(),

    # Workshop, machine shop: a fabricator, a recycler and benches round an open floor, a hazard line at the shutters
    # and a store of parts down one side.
    Prefab('workshop', 'a', 'Workshop: machine shop', '''
#####W#####
#F.t.u.R.k#
#q.......T#
Wl.......P#
#m.......x#
#c..!!!..x#
####SSS####
 ---___---
''', legend(
        F=f('WFOutpostFabricator', decals=['Bot']),
        R=f('WFOutpostRecycler', decals=['Bot']),
        t=f('TableReinforced', loose=['Wrench']),
        u=f('TableReinforced', loose=['ToolboxMechanicalFilled']),
        T=f('ClosetToolFilled'),
        k=f('Rack', loose=['SheetSteel10']),
        q={'mount': ['PosterLegitSafetyEyeProtection']},
        m=f('WFOutpostMultiCharger'),
        c=f('TableReinforced', loose=['WeaponCapacitorRecharger']),
        x=f('CrateGenericSteel', decals=['Delivery']),
        **{'!': {'decals': [EDGE['S']]}},
    )).feed(),

    # Workshop, garage: the machines along the back wall and a marked bay behind wide shutters to work on something big.
    Prefab('workshop', 'b', 'Workshop: garage', '''
###W###W###
#kk.t.uF.T#
#q.......R#
Wl.......P#
#m.+++++.x#
#c.+++++.x#
###SSSSS###
 --_____--
''', legend(
        F=f('WFOutpostFabricator', decals=['Bot']),
        R=f('WFOutpostRecycler', decals=['Bot']),
        t=f('TableReinforced', loose=['Wrench']),
        u=f('TableReinforced', loose=['ToolboxMechanicalFilled']),
        T=f('ClosetToolFilled'),
        k=f('Rack', loose=['SheetSteel10']),
        q={'mount': ['PosterLegitBuild']},
        m=f('WFOutpostMultiCharger'),
        c=f('TableReinforced', loose=['WeaponCapacitorRecharger']),
        x=f('CrateGenericSteel', decals=['Delivery']),
        **{'+': {'decals': ['Bot']}},
    )).feed(),

    # Powerhouse, battery house: batteries indoors behind a hazard line, with a desk for the engineer; two solar
    # strings and a wind turbine in the yard behind.
    Prefab('powerhouse', 'a', 'Powerhouse: battery house', '''
=s=s===T=
#########
#B..L..B#
#!!!!!!!#
Wlth..kP#
####D####
''', legend(
        s=yard('WFOutpostSolarString', mv=True),
        T=yard('WFOutpostWindTurbine', mv=True),
        B=f('WFOutpostBatterySmall', mv=True),
        L=f('WFOutpostBatteryLarge', mv=True),
        t=f('TableReinforced', loose=['Multitool']),
        h=f('Chair', face='W'),
        k=f('Rack', loose=['CableApcStack10']),
        **{'!': {'decals': [EDGE['S']]}},
    )).run(1, 5, 7, 5).run(1, 3, 7, 3).run(7, 1, 7, 5).run(4, 0, 4, 1).run(4, 1, 8, 1),

    # Powerhouse, generator shed: the batteries along the back wall with a tool closet, a plant and a desk in front.
    Prefab('powerhouse', 'b', 'Powerhouse: generator shed', '''
==s=s=T==
#########
#B.L.B.k#
#!!!!!.l#
Woth..CP#
####D####
''', legend(
        s=yard('WFOutpostSolarString', mv=True),
        T=yard('WFOutpostWindTurbine', mv=True),
        B=f('WFOutpostBatterySmall', mv=True),
        L=f('WFOutpostBatteryLarge', mv=True),
        t=f('TableReinforced', loose=['Multitool']),
        h=f('Chair', face='W'),
        k=f('Rack', loose=['CableApcStack10']),
        o=f('PottedPlant1'),
        C=f('ClosetToolFilled'),
        **{'!': {'decals': [EDGE['S']]}},
    )).run(2, 5, 6, 5).run(5, 1, 5, 5).run(1, 3, 5, 3).run(4, 0, 4, 1).run(4, 1, 8, 1),

    # Medbay, ward: curtained beds with a drip, a stasis bed and a stocked locker, and a waiting corner by the door.
    Prefab('medbay', 'a', 'Medbay: ward', '''
###W###W###
#m,i,m,s,k#
#u,,,u,,,,#
W,,,,,,,,P#
#l,,,,,,,d#
#q,q,,o,t,#
#####D#####
 -B-_----p
''', legend(
        m=f('MedicalBed'),
        i=f('WFWolfmedIvDrip'),
        s=f('StasisBed', decals=['Bot']),
        k=f('LockerMedicineFilled'),
        u=f('HospitalCurtainsOpen'),
        q=f('ShiptestChairBenchBlue', face='N'),
        o=f('PottedPlant10'),
        d={'mount': ['DefibrillatorCabinetFilled']},
        t=f('TableReinforced', loose=['MedkitFilled']),
    ), floor=WHITE).feed(),

    # Medbay, clinic: a reception with a desk and a bench out front, and the treatment room behind a door.
    Prefab('medbay', 'b', 'Medbay: clinic', '''
###W###W###
#m,i,s,m,k#
#,,,,,,,,,#
#l,,,,,,,P#
####D######
Wq,,,,h,,oW
#q,l,,rr,d#
#####D#####
 -B-_----p
''', legend(
        m=f('MedicalBed'),
        i=f('WFWolfmedIvDrip'),
        s=f('StasisBed', decals=['Bot']),
        k=f('LockerMedicineFilled'),
        q=f('ShiptestChairBenchBlue', face='E'),
        h=f('ChairOfficeLight'),
        r=f('TableGlass', loose=['Paper']),
        o=f('PottedPlant10'),
        d={'mount': ['DefibrillatorCabinetFilled']},
    ), floor=WHITE).feed(),

    # Vault, strongroom: deposit boxes and racks behind an airlock, a deposit terminal, crates on marked bays, and a
    # counting table.
    Prefab('vault', 'a', 'Vault: strongroom', '''
#########
#x.k.k.x#
#.......#
#l.th..P#
#c.d...c#
####A####
 ---_---
''', legend(
        x=f('SafetyDepositBoxLarge'),
        k=f('Rack'),
        d=f('WFOutpostDepositTerminal', decals=['Bot']),
        c=f('CrateSecure', decals=['Delivery']),
        t=f('TableReinforced', loose=['Paper', 'Pen']),
        h=f('ChairOfficeDark', face='W'),
    )).feed(),

    # Vault, counting house: a front office with a desk, and the strongroom behind a second airlock.
    Prefab('vault', 'b', 'Vault: counting house', '''
#########
#x.cdc.x#
#l......#
####A####
Wkth...P#
#l...o.i#
####D####
 -B-_---
''', legend(
        x=f('SafetyDepositBoxLarge'),
        c=f('CrateSecure', decals=['Delivery']),
        d=f('WFOutpostDepositTerminal', decals=['Bot']),
        k=f('Bookshelf'),
        t=f('TableWood', loose=['Paper', 'Pen']),
        h=f('ChairOfficeDark', face='W'),
        o=f('PottedPlant1'),
        i=f('filingCabinet'),
    )).feed(),

    # Company wing, hall: a dormitory of cryopods, a carpeted meeting hall with a broadcast console and a telecom
    # relay, and a store room.
    Prefab('company_wing', 'a', 'Company wing: hall', '''
######W###W###W######
#c;c;c#b.o...o.r#k.k#
#;;;;;#.~~~~~~~.#..l#
#;;;;;D.~hhhhh~.D...#
W^;;;;#.~ttttt~.#..P#
#w;;;e#.~HHHHH~.#x.x#
#;;;;;#.~~~~~~~.#...#
#j;;;j#l.......l#k.k#
######W##AAA##W######
 --B----___----B---p
''', legend(
        c=a('WFOutpostCryopod'),
        w=a('WardrobeMixed'),
        e=a('Dresser'),
        j=a('ComfyChair'),
        b=f('WFOutpostBroadcastConsole'),
        r=f('WFOutpostTelecomRelay'),
        o=f('PottedPlant1'),
        **{'~': a('CarpetBlue')},
        t=a('CarpetBlue', 'TableWood', loose=['Paper']),
        h=a('CarpetBlue', 'ChairOfficeDark'),
        H=a('CarpetBlue', 'ChairOfficeDark', face='N'),
        k=f('Rack', loose=['SheetSteel10']),
        x=f('CrateGenericSteel', decals=['Delivery']),
    )).feed(),

    # Company wing, lodge: a lounge with a fireplace, a sofa and a bar, an office round a meeting table, and the dormitory.
    Prefab('company_wing', 'b', 'Company wing: lodge', '''
###W###W#####W###W###
#o.F..k#b...r#c;c;c;#
#.~~~~.#.....#;;;;;;#
#.zMs~.D.hhh.D;;;;;;#
W.~~~~.#.ttt.#w;;;;^W
#luuu..#.HHH.#e;;;;P#
#nnn.J.#l....#j;;;;j#
###W####AAA####W#####
 --B----___----B---p
''', legend(
        c=a('WFOutpostCryopod'),
        w=a('WardrobeMixed'),
        e=a('Dresser'),
        j=a('ComfyChair'),
        b=f('WFOutpostBroadcastConsole'),
        r=f('WFOutpostTelecomRelay'),
        o=f('PottedPlant5'),
        F=f('Fireplace'),
        k=f('Bookshelf'),
        **{'~': a('CarpetOrange')},
        z=a('CarpetOrange', 'SofaEndRightBrown', face='N'),
        M=a('CarpetOrange', 'SofaMiddleBrown', face='N'),
        s=a('CarpetOrange', 'SofaEndLeftBrown', face='N'),
        u=f('StoolBar'),
        n=f('TableCounterWood', loose=['DrinkMugOne']),
        J=f('Jukebox'),
        t=f('TableWood', loose=['Paper']),
        h=f('ChairOfficeDark'),
        H=f('ChairOfficeDark', face='N'),
    )).feed(),

    # Canteen, mess hall: a kitchen with a range, a griddle, a freezer and a rations printer, and four tables to eat at.
    Prefab('canteen', 'a', 'Canteen: mess hall', '''
###W###W###W#
#RGk#.tt.tt.#
#,,,#.hh.hh.W
W,,,D.......#
#,,,#.tt.tt.#
#iFn#lhh.hhP#
#####W##D####
 -B----_---p
''', legend(
        R=w('WFOutpostRange'),
        G=w('WFOutpostGriddle'),
        F=w('LockerFreezer'),
        k=w('WFOutpostRationsPrinter'),
        n=w('TableCounterMetal', loose=['FoodPlate']),
        c=w('TableCounterMetal', loose=['KitchenKnife']),
        t=f('TableWood', loose=['FoodPlateSmall']),
        h=f('ChairWood', face='N'),
    )).feed(),

    # Canteen, diner: the kitchen along the back wall behind a counter with stools, and tables by the windows.
    Prefab('canteen', 'b', 'Canteen: diner', '''
###W###W###W#
#RGFk,,,,,,i#
#,,,,,,,,,,,#
Wnnnnnnnn,,,W
#uuuuuuuu...#
#lhtth...htP#
#####W##D####
 -B----_---p
''', legend(
        R=w('WFOutpostRange'),
        G=w('WFOutpostGriddle'),
        F=w('LockerFreezer'),
        k=w('WFOutpostRationsPrinter'),
        n=w('TableCounterMetal', loose=['DrinkGlass']),
        u=f('StoolBar'),
        t=f('TableWood', loose=['FoodPlateSmall']),
        h=f('ChairWood'),
    )).feed(),

    # Farm, greenhouse: eight hydroponics trays under glass, a water tank, a nutrient synthesizer, an animal feeder
    # and a bench of tools.
    Prefab('farm', 'a', 'Farm: greenhouse', '''
#W#W#W#W#W#
#y.y.y.y.T#
W.........W
#y.y.y.y.N#
W.........W
#l.kt...EP#
#####D#####
 ---_----p
''', legend(
        y=f('hydroponicsTray'),
        T=f('WaterTankFull'),
        N=f('WFOutpostNutrientSynthesizer'),
        E=f('WFOutpostAnimalFeeder'),
        k=f('Rack', loose=['HydroponicsToolMiniHoe', 'HydroponicsToolSpade']),
        t=f('TableWood', loose=['Bucket']),
    )).feed(),

    # Farm, field: twelve soil plots in the open beside a tool shed with the synthesizer, a water tank and a feeder.
    Prefab('farm', 'b', 'Farm: field', '''
ggggggggggg
googoogoogg
googoogoogg
ggggggggggg
#####gggTgg
#kN.#ggggEg
#lP.Dgggggg
#####gggggg
''', legend(
        o=lawn(ents=['hydroponicsSoil']),
        T=lawn(ents=['WaterTankFull']),
        E=lawn(ents=['WFOutpostAnimalFeeder']),
        N=f('WFOutpostNutrientSynthesizer'),
        k=f('Rack', loose=['HydroponicsToolMiniHoe', 'HydroponicsToolSpade', 'Bucket']),
    )).feed(),

    # Garden, courtyard: after the hospital's, two paths crossing at a fountain between four lawns, with benches.
    Prefab('garden', 'a', 'Garden: courtyard', '''
gvgug-gugvg
ggBgg-ggBgg
gugvg-gvgug
ggggg-ggggg
-----F-----
ggggg-ggggg
gugvg-gvgug
ggBgg-ggBgg
gvgug-gugvg
''', legend(
        v=lawn('Flowersy1'),
        u=lawn('Bushe1'),
        F=porch('DarkPackFountain'),
    ), wear=False),

    # Garden, walk: a paved walk round a flower bed, with a fountain and benches in the middle.
    Prefab('garden', 'b', 'Garden: walk', '''
-----------
-vuvuvuvuv-
-ugggggggu-
-vgB-F-Bgv-
-ugggggggu-
-vuvuvuvuv-
-----------
''', legend(
        v=lawn('Flowersbr1'),
        u=lawn('Bushe3'),
        F=porch('DarkPackFountain'),
    ), wear=False),

    # Lounge, bar: a counter with stools, a jukebox, a sofa on a rug by the fire and a games table.
    Prefab('lounge', 'a', 'Lounge: bar', '''
###W###W###W#
#k.nnnnn..Jo#
#..uuuuu....W
W...........#
#F.~~~~..tt.#
#l.zMs~..hhP#
#####W##D####
 -B----_---p
''', legend(
        k=f('Bookshelf'),
        n=f('TableCounterWood', loose=['DrinkGlass']),
        u=f('StoolBar'),
        J=f('Jukebox'),
        o=f('PottedPlant5'),
        F=f('Fireplace'),
        **{'~': a('CarpetPurple')},
        z=a('CarpetPurple', 'SofaEndRightBrown', face='N'),
        M=a('CarpetPurple', 'SofaMiddleBrown', face='N'),
        s=a('CarpetPurple', 'SofaEndLeftBrown', face='N'),
        t=f('TableWood', loose=['ChessBoard']),
        h=f('ChairWood', face='N'),
    )).feed(),

    # Lounge, rec room: games tables, a jukebox, a darts target and a sofa with armchairs round a rug.
    Prefab('lounge', 'b', 'Lounge: rec room', '''
###W###W###W#
#o.tt.tt.kk.#
#..hh.hh....W
W...........#
#J.~~~~~.e.e#
#l.~zMs~.d.P#
#####W##D####
 -B----_---p
''', legend(
        o=f('PottedPlant1'),
        t=f('TableWood', loose=['ParchisBoard']),
        h=f('ChairWood', face='N'),
        k=f('Bookshelf'),
        J=f('Jukebox'),
        **{'~': a('CarpetGreen')},
        z=a('CarpetGreen', 'SofaEndRightBrown', face='N'),
        M=a('CarpetGreen', 'SofaMiddleBrown', face='N'),
        s=a('CarpetGreen', 'SofaEndLeftBrown', face='N'),
        e=f('ComfyChair'),
        d=f('TargetDarts'),
    )).feed(),

    # Library, reading room: shelves along the walls, two desks with lamps and armchairs on a rug.
    Prefab('library', 'a', 'Library: reading room', '''
###W###W###
#kkk.kkk.o#
#.........#
Wt.~~~~~.tW
#h.e~~~e.h#
#l.....k.P#
#####D#####
 -B-_----p
''', legend(
        k=f('Bookshelf'),
        o=f('PottedPlant1'),
        t=f('TableWood', loose=['LampGold']),
        h=f('ChairWood', face='N'),
        **{'~': a('CarpetBlue')},
        e=a('CarpetBlue', 'ComfyChair'),
    )).feed(),

    # Library, study: an office with a desk, filing cabinets and a painting, and a row of shelves behind.
    Prefab('library', 'b', 'Library: study', '''
###W###W###
#kkkkkkkkk#
#.........#
#k.~~h~~.k#
Wk.~tdt~.oW
#i.~~~~~lP#
#####D#####
 -B-_----p
''', legend(
        k=f('Bookshelf'),
        o=f('PottedPlant5'),
        **{'~': a('CarpetBlack')},
        t=a('CarpetBlack', 'TableWood', loose=['Paper']),
        d=a('CarpetBlack', 'TableWood', loose=['LampGold', 'Pen']),
        h=a('CarpetBlack', 'ChairOfficeDark'),
        i=f('filingCabinet', mount=['PaintingTheGreatWave']),
    )).feed(),
]

COLOURS = {
    FLOOR: (118, 122, 128), ACCENT: (146, 126, 104), WHITE: (196, 200, 204), PLATE: (72, 74, 80), BOLTS: (96, 100, 108),
    PORCH: (150, 154, 160), WALL: (38, 40, 46), WINDOW: (110, 170, 200), DOOR: (150, 110, 70), AIRLOCK: (190, 130, 60),
    SHUTTERS: (170, 150, 90), 'CarpetBlue': (70, 96, 150), 'CarpetGreen': (60, 120, 72), 'CarpetOrange': (176, 108, 52),
    'CarpetPurple': (120, 80, 150), 'CarpetBlack': (40, 40, 46), GRASS: (72, 128, 64),
}

DECAL_COLOURS = {'Warn': (230, 190, 40), 'Dirt': (90, 70, 50), 'Flowers': (230, 200, 70), 'Bush': (40, 90, 40), 'Bot': (220, 220, 220), 'Delivery': (220, 220, 220)}
DRAWN_EDGE = {decal: side for side, decal in EDGE.items()}


def known_ids():
    """Entity and decal prototype ids."""
    entities, decals = set(), set()
    for root, _, files in os.walk('Resources/Prototypes'):
        for name in files:
            if not name.endswith('.yml'):
                continue
            with open(os.path.join(root, name), encoding='utf-8', errors='ignore') as handle:
                text = handle.read()
            for kind, proto in re.findall(r'^- type: (\w+)\n(?:  .*\n)*?  id: (\S+)', text, re.M):
                if kind == 'entity':
                    entities.add(proto)
                elif kind == 'decal':
                    decals.add(proto)
    return entities, decals


def draw(prefab, poi, scale=28):
    from PIL import Image, ImageDraw
    image = Image.new('RGB', (max(prefab.width * scale, 300), prefab.height * scale + 22), (24, 26, 30))
    pen = ImageDraw.Draw(image)
    pen.text((4, 4), f'{prefab.title}  {prefab.width}x{prefab.height}  {len(poi.entities)} objects', fill=(230, 230, 230))
    for (x, y), tile in poi.tiles.items():
        if tile == 'Space':
            continue
        left, top = x * scale, (prefab.height - 1 - y) * scale + 22
        pen.rectangle([left, top, left + scale - 1, top + scale - 1], fill=COLOURS.get(tile, (90, 90, 90)), outline=(50, 52, 58))
        if (x, y) not in poi.roofs:
            pen.line([left + 3, top + scale - 4, left + scale - 4, top + 3], fill=(140, 150, 160))
    for decal, x, y, _ in poi.decals:
        left, top = x * scale, (prefab.height - 1 - y) * scale + 22
        colour = next((value for key, value in DECAL_COLOURS.items() if decal.startswith(key)), (200, 200, 200))
        edge = {'N': [left, top, left + scale - 1, top + 2], 'S': [left, top + scale - 3, left + scale - 1, top + scale - 1],
                'W': [left, top, left + 2, top + scale - 1], 'E': [left + scale - 3, top, left + scale - 1, top + scale - 1]}
        if decal in DRAWN_EDGE:
            pen.rectangle(edge[DRAWN_EDGE[decal]], fill=colour)
        elif decal.startswith(('Dirt', 'Flowers', 'Bush')):
            pen.ellipse([left + 8, top + 8, left + scale - 9, top + scale - 9], fill=colour)
        else:
            pen.rectangle([left + 2, top + 2, left + scale - 3, top + scale - 3], outline=colour)
    cables = {'CableApcExtension': (90, 200, 90), 'CableMV': (230, 200, 60)}
    for proto, x, y, _, anchored, _ in poi.entities:
        left, top = x * scale, (prefab.height - 1 - y) * scale + 22
        if proto in cables:
            offset = 4 if proto == 'CableMV' else 9
            pen.rectangle([left + offset, top + scale - 6, left + offset + 3, top + scale - 3], fill=cables[proto])
        elif proto in COLOURS:
            pen.rectangle([left + 1, top + 1, left + scale - 2, top + scale - 2], fill=COLOURS[proto])
        else:
            short = re.sub(r'^(WFHideoutShipwright|WFHideoutHangar|WFOutpost|WFHideout|WFWolfmed|WF)', '', proto)[:4] or proto[:4]
            pen.rectangle([left + 3, top + 3, left + scale - 4, top + scale - 10],
                          fill=(60, 110, 170) if anchored else (170, 90, 150))
            pen.text((left + 3, top + 5), short, fill=(255, 255, 255))
    return image


def main():
    entities, decals = known_ids()
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(os.path.dirname(SHEET), exist_ok=True)
    images = []
    for prefab in PREFABS:
        poi = prefab.build()
        missing = sorted({entity[0] for entity in poi.entities} - entities) + sorted({decal[0] for decal in poi.decals} - decals)
        if missing:
            raise SystemExit(f'{prefab.name}: unknown prototypes {missing}')
        with open(os.path.join(OUT, prefab.name + '.yml'), 'w', encoding='utf-8', newline='\n') as handle:
            handle.write(poi.serialize())
        print(f'{prefab.name}: {prefab.width}x{prefab.height}, {len(poi.entities)} objects, {len(poi.decals)} decals')
        images.append(draw(prefab, poi))

    from PIL import Image
    aprons, rooms = images[:2], images[2:]
    rows = [aprons] + [rooms[i:i + 4] for i in range(0, len(rooms), 4)]
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
