#!/usr/bin/env python3
"""Converts Bubberstation's tall (32x64), wide (64x32) and large (64x64) borg DMIs into borg subtype RSIs.

    python Tools/_WG/BorgSkins/convert.py <dir with the .dmi files> <Bubberstation commit sha>

Rewrites Resources/Textures/_WG/BorgSkins, the subtype and chassis prototypes and the chassis names.
Needs Pillow. The folder holds modular_skyrat/modules/borgs/icons/{tallrobot,widerobot}*.dmi and, in a zubbers
subfolder, modular_zubbers/code/modules/silicons/borgs/sprites: tallrobot*.dmi (the dullahans) and the
kittycatborgs, myomer and wyvern folders with their CREDIT and LICENSE files. A readme.md next to the skyrat sheets
(from the tallborg artist) is copied along.
"""
import json
import math
import re
import shutil
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
TEXTURES = ROOT / 'Resources/Textures/_WG/BorgSkins'
PROTOTYPES = ROOT / 'Resources/Prototypes/_WG/BorgSkins'
LOCALE = ROOT / 'Resources/Locale/en-US/_WG/BorgSkins'
SOURCE = 'https://github.com/Bubberstation/Bubberstation/blob/{sha}/{folder}/{file}'
SKYRAT = 'modular_skyrat/modules/borgs/icons'
ZUBBERS = 'modular_zubbers/code/modules/silicons/borgs/sprites'

# Bubber department -> (folder and ID part, borg type, word used in the chassis name).
# Departments with no borg type here are offered under the generic type.
DEPTS = {
    'eng': ('Engineering', 'engineering', 'engineering'),
    'jani': ('Janitor', 'janitor', 'janitor'),
    'med': ('Medical', 'medical', 'medical'),
    'mine': ('Mining', 'mining', 'salvage'),
    'serv': ('Service', 'service', 'service'),
    'cargo': ('Cargo', 'generic', 'cargo'),
    'sec': ('Security', 'generic', 'security'),
    'pk': ('Peacekeeper', 'generic', 'peacekeeper'),
    'ninja': ('Ninja', 'generic', 'ninja'),
    'syndi': ('Syndicate', 'generic', 'syndicate'),
    'clown': ('Clown', 'generic', 'clown'),
    'sci': ('Research', 'generic', 'research'),
    'tarkon': ('Tarkon', 'generic', 'Tarkon'),
}

# Borg type -> (body state, the type has a spriteBodyMovementState).
TYPES = {
    'generic': ('robot', False),
    'engineering': ('engineer', False),
    'mining': ('miner', True),
    'janitor': ('janitor', True),
    'medical': ('medical', True),
    'service': ('service', False),
}

# kind (tall, wide, or dullahan: tall, from the zubbers sheets; the PACKS kinds are added below), department, skin name, icon state, state the eye and lamp overlays come from if not its own.
# Names and light keys follow Bubberstation's borg_skins lists.
SKINS = [
    ('tall', 'eng', 'Meka', 'mekaengi', None),
    ('tall', 'eng', 'K4T (Technician)', 'k4tengi', None),
    ('tall', 'eng', 'K4T (Hazard)', 'k4tengi_alt1', None),
    ('tall', 'eng', 'NiKA', 'fmekaeng', None),
    ('tall', 'eng', 'NiKO', 'mmekaeng', None),
    ('tall', 'eng', 'NiKO (Alt)', 'sfmekaengi', None),
    ('tall', 'jani', 'Meka', 'mekajani', None),
    ('tall', 'jani', 'K4T (Viscera)', 'k4tjani', None),
    ('tall', 'jani', 'K4T (Domestic)', 'k4tjani_alt1', None),
    ('tall', 'jani', 'NiKA', 'fmekajani', None),
    ('tall', 'jani', 'NiKO', 'mmekajani', None),
    ('tall', 'med', 'Meka', 'mekamed', None),
    ('tall', 'med', 'K4T (Doc)', 'k4tmed', None),
    ('tall', 'med', 'K4T (Field Medic)', 'k4tmed_alt1', None),
    ('tall', 'med', 'NiKA', 'fmekamed', None),
    ('tall', 'med', 'NiKO', 'mmekamed', None),
    ('tall', 'med', 'NiKO (Alt)', 'sfmekamed', None),
    ('tall', 'mine', 'Meka', 'mekamine', None),
    ('tall', 'mine', 'K4T (Rookie)', 'k4tmine', None),
    ('tall', 'mine', 'K4T (Veteran)', 'k4tmine_alt1', None),
    ('tall', 'mine', 'NiKA', 'fmekamine', None),
    ('tall', 'mine', 'NiKO', 'mmekamine', None),
    ('tall', 'mine', 'NiKA (Alt)', 'sfmekamine', None),
    ('tall', 'serv', 'Meka', 'mekaserve', None),
    ('tall', 'serv', 'Meka (Alt)', 'mekaserve_alt', 'mekaserve'),
    ('tall', 'serv', 'K4T (Barista)', 'k4tserve', None),
    ('tall', 'serv', 'K4T (80s)', 'k4tserve_alt1', None),
    ('tall', 'serv', 'K4T (Dispenser)', 'k4tserve_alt2', None),
    ('tall', 'serv', 'NiKA', 'fmekaserv', None),
    ('tall', 'serv', 'NiKO', 'mmekaserv', None),
    ('tall', 'serv', 'NiKO (Alt)', 'sfmekaserv', None),
    ('tall', 'cargo', 'Meka', 'mekacargo', None),
    ('tall', 'cargo', 'K4T (Loader)', 'k4tcargo', None),
    ('tall', 'cargo', 'K4T (Turtleneck)', 'k4tcargo_alt1', None),
    ('tall', 'cargo', 'NiKA', 'fmekacargo', None),
    ('tall', 'cargo', 'NiKO', 'mmekacargo', None),
    ('tall', 'cargo', 'NiKO (Alt)', 'sfmekacargo', None),
    ('tall', 'sec', 'Meka', 'mekasec', None),
    ('tall', 'sec', 'K4T', 'k4tsec', None),
    ('tall', 'sec', 'NiKA', 'fmekasec', None),
    ('tall', 'sec', 'NiKO', 'mmekasec', None),
    ('tall', 'pk', 'Meka', 'mekapeace', None),
    ('tall', 'pk', 'K4T', 'k4tpeace', None),
    ('tall', 'pk', 'NiKA', 'fmekapeace', None),
    ('tall', 'pk', 'NiKO', 'mmekapeace', None),
    ('tall', 'pk', 'NiKO (Alt)', 'sfmekapeace', None),
    ('tall', 'ninja', 'Meka', 'mekaninja', None),
    ('tall', 'ninja', 'K4T', 'k4tninja', None),
    ('tall', 'ninja', 'NiKA', 'fmekaninja', None),
    ('tall', 'ninja', 'NiKO', 'mmekaninja', None),
    ('tall', 'syndi', 'Meka', 'mekasyndi', None),
    ('tall', 'syndi', 'K4T', 'k4tsyndi', None),
    ('tall', 'syndi', 'NiKA', 'fmekasyndi', None),
    ('tall', 'syndi', 'NiKO', 'mmekasyndi', None),
    ('tall', 'clown', 'K4T', 'k4tclown', None),
    ('wide', 'eng', 'Drake', 'drakeeng', None),
    ('wide', 'eng', 'Vale', 'valeeng', None),
    ('wide', 'eng', 'Hound', 'engihound', None),
    ('wide', 'eng', 'Darkhound', 'engihounddark', None),
    ('wide', 'eng', 'Borgi', 'borgi-eng', None),
    ('wide', 'eng', 'Otie', 'otiee', None),
    ('wide', 'eng', 'Pup Dozer', 'pupdozer', None),
    ('wide', 'eng', 'Alina', 'alina-eng', 'alina'),
    ('wide', 'jani', 'Drake', 'drakejanit', None),
    ('wide', 'jani', 'Vale', 'J9', None),
    ('wide', 'jani', 'Scrubpuppy', 'scrubpup', None),
    ('wide', 'jani', 'Borgi', 'borgi-jani', None),
    ('wide', 'jani', 'Otie', 'otiej', None),
    ('wide', 'med', 'Drake', 'drakemed', None),
    ('wide', 'med', 'Vale', 'valemed', None),
    ('wide', 'med', 'Hound', 'medihound', None),
    ('wide', 'med', 'Darkhound', 'medihounddark', None),
    ('wide', 'med', 'Borgi', 'borgi-medi', None),
    ('wide', 'med', 'Alina', 'alina-med', 'alina'),
    ('wide', 'mine', 'Drake', 'drakemine', None),
    ('wide', 'mine', 'Vale', 'valemine', None),
    ('wide', 'mine', 'Hound', 'cargohound', None),
    ('wide', 'mine', 'Darkhound', 'cargohounddark', None),
    ('wide', 'mine', 'Otie', 'otiec', None),
    ('wide', 'mine', 'Blade', 'blade', None),
    ('wide', 'serv', 'Drake', 'drakeserv', None),
    ('wide', 'serv', 'Vale', 'valeserv', None),
    ('wide', 'serv', 'Vale (Dark)', 'valeservdark', None),
    ('wide', 'serv', 'Darkhound', 'k50', None),
    ('wide', 'serv', 'Partyhound', 'k69', None),
    ('wide', 'serv', 'Borgi', 'borgi-serv', None),
    ('wide', 'cargo', 'Drake', 'drakecargo', None),
    ('wide', 'cargo', 'Vale', 'valecargo', None),
    ('wide', 'cargo', 'Hound', 'cargohound', None),
    ('wide', 'cargo', 'Darkhound', 'cargohounddark', None),
    ('wide', 'cargo', 'Borgi', 'borgi-cargo', None),
    ('wide', 'sec', 'Drake', 'drakesec', None),
    ('wide', 'sec', 'Vale', 'valesec', None),
    ('wide', 'sec', 'Hound', 'k9', None),
    ('wide', 'sec', 'Darkhound', 'k9dark', None),
    ('wide', 'sec', 'Borgi', 'borgi-sec', None),
    ('wide', 'sec', 'Otie', 'oties', None),
    ('wide', 'sec', 'Alina', 'alina-sec', 'alina'),
    ('wide', 'pk', 'Drake', 'drakepeace', None),
    ('wide', 'pk', 'Vale', 'valepeace', None),
    ('wide', 'pk', 'Borgi', 'borgi', None),
    ('dullahan', 'eng', 'Dullahan', 'dullahaneng', None),
    ('dullahan', 'eng', 'Dullahan (Taur)', 'dullahantaureng', None),
    ('dullahan', 'jani', 'Dullahan', 'dullahanjani', None),
    ('dullahan', 'jani', 'Dullahan (Taur)', 'dullahantaurjani', None),
    ('dullahan', 'med', 'Dullahan', 'dullahanmed', None),
    ('dullahan', 'med', 'Dullahan (Taur)', 'dullahantaurmed', None),
    ('dullahan', 'mine', 'Dullahan', 'dullahanmine', None),
    ('dullahan', 'mine', 'Dullahan (Taur)', 'dullahantaurmine', None),
    ('dullahan', 'serv', 'Dullahan', 'dullahanserv', None),
    ('dullahan', 'serv', 'Dullahan (Alt)', 'dullahanserv_alt', None),
    ('dullahan', 'serv', 'Dullahan (Taur)', 'dullahantaurserv', None),
    ('dullahan', 'cargo', 'Dullahan', 'dullahancargo', None),
    ('dullahan', 'cargo', 'Dullahan (Taur)', 'dullahantaurcargo', None),
    ('dullahan', 'sec', 'Dullahan', 'dullahansec', None),
    ('dullahan', 'pk', 'Dullahan', 'dullahanpeace', None),
    ('dullahan', 'pk', 'Dullahan (Taur)', 'dullahantaurpeace', None),
    ('dullahan', 'ninja', 'Dullahan', 'dullahanninja', None),
    ('dullahan', 'ninja', 'Dullahan (Taur)', 'dullahantaurninja', None),
    ('dullahan', 'syndi', 'Dullahan', 'dullahansyndi', None),
    ('dullahan', 'syndi', 'Dullahan (Taur)', 'dullahantaursyndi', None),
    ('dullahan', 'clown', 'Dullahan', 'dullahanclown', None),
    ('dullahan', 'clown', 'Dullahan (Taur)', 'dullahantaurclown', None),
    ('dullahan', 'sci', 'Dullahan', 'dullahansci', None),
    ('dullahan', 'tarkon', 'Dullahan', 'dullahantarkon', None),
]

FILES = {
    ('tall', d): f'tallrobot_{d}.dmi' for d in DEPTS
} | {
    ('wide', d): ('widerobots_cargo.dmi' if d == 'cargo' else f'widerobot_{d}.dmi') for d in DEPTS
} | {
    ('dullahan', d): f'zubbers/tallrobot_{"syndie" if d == "syndi" else d}.dmi' for d in DEPTS
}

# Skin sets with a folder, a sheet per department and a licence of their own:
# kind -> (skin name, zubbers folder, sheet name pattern, {department: (sheet name part, icon state)}).
PACKS = {
    'kitty': ('K1-T7Y', 'kittycatborgs/kittyborg', 'Kittyborg_{}.dmi', {
        'eng': ('engi', 'engi'), 'jani': ('jani', 'jani'), 'med': ('medicat', 'medicat'), 'mine': ('mine', 'mining'),
        'serv': ('service', 'service'), 'sec': ('sec', 'sec'), 'sci': ('sci', 'sci'),
    }),
    'cat': ('C9-T7O', 'kittycatborgs/catborgs', 'catborg_{}.dmi', {
        'eng': ('engineering', 'engi'), 'med': ('medical', 'meowdical'), 'mine': ('mining', 'mining'),
        'serv': ('service', 'service'), 'sec': ('security', 'sec'), 'sci': ('science', 'sci'),
    }),
    'myomer': ('Myomer', 'myomer', 'ProjectMyomer{}.dmi', {
        'eng': ('Engi', 'myomer-engi'), 'jani': ('Jani', 'myomer-jani'), 'med': ('Medical', 'myomer-med'),
        'mine': ('Mining', 'myomer-mine'), 'serv': ('Service', 'myomer-serv'), 'cargo': ('Cargo', 'myomer-cargo'),
        'sec': ('Sec', 'myomer-sec'), 'pk': ('PeaceKeeper', 'myomer-pk'), 'ninja': ('Ninja', 'myomer-ninja'),
        'syndi': ('Syndi', 'myomer-syndi'), 'sci': ('Sci', 'myomer-sci'),
    }),
    'wyvern': ('Wyvern', 'wyvern', 'WyvernFrame{}.dmi', {
        'eng': ('Enginerding', 'wyvern-engi'), 'jani': ('Jani', 'wyvern-jani'), 'med': ('Medical', 'wyvern-medical'),
        'mine': ('Mining', 'wyvern-mine'), 'sec': ('Sec', 'wyvern-sec'), 'pk': ('Peace', 'wyvern-pk'),
        'syndi': ('Syndi', 'wyvern-syndi'), 'sci': ('Research', 'wyvern-sci'),
    }),
}
for _kind, (_name, _folder, _pattern, _depts) in PACKS.items():
    for _dept, (_part, _icon) in _depts.items():
        SKINS.append((_kind, _dept, _name, _icon, None))
        FILES[_kind, _dept] = f'zubbers/{_folder}/{_pattern.format(_part)}'

# Texture folder per kind, by sprite size. Anything taller than a tile is raised half a tile to stand on it.
SIZES = {'tall': 'Tall', 'dullahan': 'Tall', 'wide': 'Wide', 'kitty': 'Wide', 'cat': 'Wide', 'myomer': 'Wide',
         'wyvern': 'Large'}

# Extra poses kept in each skin's RSI. WGBorgSkinSystem shows wreck and tipped by itself; the voluntary ones are
# listed in the skin's wgBorgSkin.poses and become actions.
POSES = ['rest', 'sit', 'bellyup', 'wreck', 'tipped']
VOLUNTARY_POSES = {'rest': 'Rest', 'sit': 'Sit', 'bellyup': 'BellyUp'}
# The eng sheet has the Alina body but not its overlays.
LIGHT_FALLBACK = {'alina': 'widerobot_med.dmi'}


class Dmi:
    """One .dmi: a PNG sheet plus a text chunk listing its states."""

    def __init__(self, path, folder):
        image = Image.open(path)
        self.name = path.name
        self.folder = folder
        self.width = self.height = 32
        self.states = {}
        self.used = set()
        state = None
        index = 0
        for line in image.info['Description'].splitlines():
            if '=' not in line:
                continue
            key, value = (part.strip() for part in line.split('=', 1))
            if key == 'width':
                self.width = int(value)
            elif key == 'height':
                self.height = int(value)
            elif key == 'state':
                if state:
                    index += state['dirs'] * state['frames']
                state = {'name': value.strip('"'), 'dirs': 1, 'frames': 1, 'delay': None, 'move': False,
                         'rewind': False}
                state['start'] = index
                self.states.setdefault(state['name'], []).append(state)
            elif state is not None:
                if key == 'dirs':
                    state['dirs'] = int(value)
                elif key == 'frames':
                    state['frames'] = int(value)
                elif key == 'delay':
                    state['delay'] = [float(x) for x in value.split(',')]
                elif key == 'movement':
                    state['move'] = value == '1'
                elif key == 'rewind':
                    state['rewind'] = value == '1'
        self.sheet = image.convert('RGBA')
        self.columns = self.sheet.width // self.width

    def find(self, name, move=False):
        for state in self.states.get(name, []):
            if state['move'] == move:
                return state
        return None

    def take(self, name, move=False):
        """Returns (frames[dir][frame], delays) for a state and marks it converted."""
        state = self.find(name, move)
        if state is None:
            return None
        self.used.add((name, move))
        order = list(range(state['frames']))
        delays = state['delay'] or [1.0] * state['frames']
        if state['rewind'] and state['frames'] > 2:
            order += order[-2:0:-1]
        frames = []
        for direction in range(state['dirs']):
            row = []
            for frame in order:
                i = state['start'] + frame * state['dirs'] + direction
                x, y = (i % self.columns) * self.width, (i // self.columns) * self.height
                row.append(self.sheet.crop((x, y, x + self.width, y + self.height)))
            frames.append(row)
        return frames, [round(delays[f] / 10, 3) for f in order]

    def blank(self):
        return [[Image.new('RGBA', (self.width, self.height))] for _ in range(4)], [0.1]


def write_state(folder, name, frames, delays):
    """Writes one RSI state sheet and returns its meta.json entry."""
    flat = [frame for row in frames for frame in row]
    width, height = flat[0].size
    columns = math.ceil(math.sqrt(len(flat)))
    sheet = Image.new('RGBA', (columns * width, math.ceil(len(flat) / columns) * height))
    for i, frame in enumerate(flat):
        sheet.paste(frame, ((i % columns) * width, (i // columns) * height))
    sheet.save(folder / f'{name}.png', optimize=True)
    entry = {'name': name}
    if len(frames) > 1:
        entry['directions'] = len(frames)
    if len(frames[0]) > 1:
        entry['delays'] = [delays] * len(frames)
    return entry


def write_meta(folder, dmi, entries, kind, icon, sha):
    meta = {
        'version': 1,
        'license': 'CC-BY-NC-SA-3.0' if kind in PACKS else 'CC-BY-SA-3.0',
        'copyright': f'{credit_for(kind, icon)}Taken from Bubberstation at {SOURCE.format(sha=sha, folder=dmi.folder, file=dmi.name)}',
        'size': {'x': dmi.width, 'y': dmi.height},
        'states': entries,
    }
    (folder / 'meta.json').write_text(json.dumps(meta, indent=2) + '\n', encoding='utf-8', newline='\n')


def credit_for(kind, icon):
    if kind in ('kitty', 'cat'):
        return ('Kittyborg and catborg by Toriate; licence holders Matica, Cameron The Raven and Spotteh '
                '(https://github.com/VOREStation/VOREStation/pull/16910). ')
    if kind == 'myomer':
        return 'Project Myomer borg by Toriate, commissioned by The Sharkenning. '
    if kind == 'wyvern':
        return 'Wyvern Frame borg by Toriate, commissioned by Gearsin. '
    if kind == 'dullahan':
        return 'Dullahan borg by Toriate and Cecily. '
    if kind == 'tall':
        if icon.startswith('k4t'):
            return 'K4T tallborg by zy.dras. '
        return 'Tallborg and MekaNIKA skinline by TheOOZ (ghostsheep). '
    if kind == 'wide' and icon.startswith('drake'):
        return 'Drake borg originally by Navier. '
    return ''


def safe(name):
    return re.sub(r'[^a-z0-9_-]+', '_', name.lower().replace('+', 'p')).strip('_')


def main():
    source, sha = Path(sys.argv[1]), sys.argv[2]
    for folder in ('Tall', 'Wide', 'Large', 'Credits'):
        shutil.rmtree(TEXTURES / folder, ignore_errors=True)
    dmis = {}

    def load(file):
        if file not in dmis:
            parts = file.split('/')[:-1]
            dmis[file] = Dmi(source / file, '/'.join([ZUBBERS] + parts[1:]) if parts else SKYRAT)
        return dmis[file]

    subtypes, chassis, names = [], [], []
    for kind, dept, name, icon, light_key in SKINS:
        folder_name, borg_type, word = DEPTS[dept]
        body, type_moves = TYPES[borg_type]
        dmi = load(FILES[kind, dept])
        ident = folder_name + ''.join(w[0].upper() + w[1:] for w in re.findall(r'[A-Za-z0-9]+', name))
        snake = re.sub(r'[^a-z0-9]+', '_', name.lower()).strip('_')
        size = SIZES[kind]
        rsi = TEXTURES / size / folder_name / f'{snake}.rsi'
        rsi.mkdir(parents=True)

        lights = light_key or icon
        light_dmi = dmi
        if dmi.find(f'{lights}_e') is None and lights in LIGHT_FALLBACK:
            light_dmi = load(LIGHT_FALLBACK[lights])

        idle = dmi.take(icon)
        if idle is None:
            raise SystemExit(f'{dmi.name} has no state {icon}')
        eyes = light_dmi.take(f'{lights}_e') or dmi.blank()
        no_mind = light_dmi.take(f'{lights}_e_r') or eyes
        lamp = light_dmi.take(f'{lights}_l') or dmi.blank()
        states = [(body, idle), (f'{body}_e', eyes), (f'{body}_e_r', no_mind), (f'{body}_l', lamp)]

        walk = dmi.take(icon, True)
        if walk:
            states += [
                (f'{body}_moving', walk),
                (f'{body}_e_moving', light_dmi.take(f'{lights}_e', True) or eyes),
                (f'{body}_e_r_moving', light_dmi.take(f'{lights}_e_r', True) or light_dmi.take(f'{lights}_e', True) or no_mind),
                (f'{body}_l_moving', light_dmi.take(f'{lights}_l', True) or lamp),
            ]
        elif type_moves:
            states.append((f'{body}_moving', idle))
        animated = any(len(frames[0]) > 1 for _, (frames, _) in states)

        offered = []
        for pose in POSES:
            taken = dmi.take(f'{icon}-{pose}')
            if taken:
                states.append((pose, taken))
                if pose in VOLUNTARY_POSES:
                    offered.append(VOLUNTARY_POSES[pose])
        if kind == 'tall':
            taken = dmi.take('k4tcredit' if icon.startswith('k4t') else 'credit')
            if taken:
                states.append(('credit', taken))

        entries = [write_state(rsi, state, frames, delays) for state, (frames, delays) in states]
        write_meta(rsi, dmi, entries, kind, icon, sha)

        subtype = f'WGBorgSkin{ident}'
        dummy = f'WGBorgChassis{ident}'
        path = rsi.relative_to(ROOT / 'Resources/Textures').as_posix()
        entry = (f'- type: borgSubtype\n  id: {subtype}\n  parentBorgType: {borg_type}\n'
                 f'  spritePath: {path}\n  dummyPrototype: {dummy}\n')
        extra = []
        if size != 'Wide':
            extra.append('  offset: 0, 0.5')
        if animated:
            extra.append('  animated: true')
        if walk:
            extra.append('  walks: true')
        if offered:
            extra.append(f"  poses: [ {', '.join(offered)} ]")
        if extra:
            entry += f'\n- type: wgBorgSkin\n  id: {subtype}\n' + '\n'.join(extra) + '\n'
        subtypes.append(((borg_type, kind), entry))
        parent = {'generic': 'BorgChassisGeneric', 'engineering': 'BorgChassisEngineer', 'mining': 'BorgChassisMining',
                  'janitor': 'BorgChassisJanitor', 'medical': 'BorgChassisMedical',
                  'service': 'BorgChassisService'}[borg_type]
        chassis.append(f'- type: entity\n  id: {dummy}\n  parent: {parent}\n  categories: [ HideSpawnMenu ]\n'
                       f'  components:\n  - type: BorgSwitchableSubtype\n    borgSubtype: {subtype}\n')
        names.append(f'ent-{dummy} = {name} {word} cyborg\n')

    # Whatever no skin claimed (cover and weapon overlays, sleeper lights, spare skins) is kept per sheet.
    for (kind, dept), file in FILES.items():
        dmi = dmis.get(file)
        if dmi is None:
            continue
        entries = []
        own = kind not in ('tall', 'wide')
        folder = TEXTURES / SIZES[kind] / DEPTS[dept][0] / (f'{kind}_extras.rsi' if own else 'extras.rsi')
        for name, variants in dmi.states.items():
            for state in variants:
                if (name, state['move']) in dmi.used or name in ('credit', 'k4tcredit'):
                    continue
                # The zubbers sheets also hold skins that aren't ported.
                if kind == 'dullahan' and not name.startswith(('dullahan', 'ov-opencover')):
                    continue
                folder.mkdir(parents=True, exist_ok=True)
                out = safe(name) + ('_moving' if state['move'] else '')
                while any(e['name'] == out for e in entries):
                    out += '_alt'
                entries.append(write_state(folder, out, *dmi.take(name, state['move'])))
        if entries:
            write_meta(folder, dmi, entries, kind if own else '', '', sha)

    # The artists' terms travel with the sprites.
    if (source / 'readme.md').exists():
        shutil.copy(source / 'readme.md', TEXTURES / 'Tall/readme.md')
    for pack in sorted({folder.split('/')[0] for _, folder, _, _ in PACKS.values()}):
        target = TEXTURES / 'Credits' / pack
        target.mkdir(parents=True)
        for file in (source / 'zubbers' / pack).iterdir():
            if file.is_file() and file.suffix.lower() in ('.md', '.txt'):
                shutil.copy(file, target / file.name)

    PROTOTYPES.mkdir(parents=True, exist_ok=True)
    LOCALE.mkdir(parents=True, exist_ok=True)
    header = '# Generated by Tools/_WG/BorgSkins/convert.py; edit the skin table there.\n\n'
    subtypes.sort(key=lambda pair: pair[0])
    (PROTOTYPES / 'subtypes.yml').write_text(header + '\n'.join(e for _, e in subtypes), encoding='utf-8', newline='\n')
    (PROTOTYPES / 'chassis.yml').write_text(header + '\n'.join(chassis), encoding='utf-8', newline='\n')
    (LOCALE / 'chassis.ftl').write_text(header + ''.join(names), encoding='utf-8', newline='\n')
    print(f'{len(SKINS)} skins from {len(dmis)} sheets')


if __name__ == '__main__':
    main()
