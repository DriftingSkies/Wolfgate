"""Builds Wolfgate's colony RSIs from NovaSector's colony_fabricator DMIs (via Meridian-Rift).

Each RSI maps output state names (what our prototypes and visualizers ask for) to (dmi, source state).
Run: python Tools/_WF/Outposts/build_colony.py <NovaSector colony_fabricator/icons dir> Resources/Textures/_WF/Outposts/Colony
"""
import json, math, os, re, sys
from PIL import Image

ICONS, OUT = (sys.argv[1], sys.argv[2]) if __name__ == '__main__' else (None, None)
COPYRIGHT = ('Taken from NovaSector (modular_nova/modules/colony_fabricator, '
             'https://github.com/NovaSector/NovaSector) via Meridian-Rift')
_cache = {}


def load(dmi):
    if dmi in _cache:
        return _cache[dmi]
    im = Image.open(os.path.join(ICONS, dmi))
    desc = im.info['Description']
    w = int(re.search(r'width = (\d+)', desc).group(1))
    h = int(re.search(r'height = (\d+)', desc).group(1))
    sheet = im.convert('RGBA')
    cols = sheet.width // w
    states, index = {}, 0
    for block in re.split(r'\nstate = ', desc)[1:]:
        name = re.match(r'"(.*)"', block).group(1)
        dirs = int(re.search(r'dirs = (\d+)', block).group(1))
        frames = int(re.search(r'frames = (\d+)', block).group(1))
        d = re.search(r'delay = ([\d.,]+)', block)
        delays = [float(x) / 10 for x in d.group(1).split(',')] if d else [0.1] * frames
        delays = (delays + [delays[-1]] * frames)[:frames]
        icons = {}
        for f in range(frames):
            for dd in range(dirs):
                i = index + f * dirs + dd
                x, y = (i % cols) * w, (i // cols) * h
                icons[(dd, f)] = sheet.crop((x, y, x + w, y + h))
        states.setdefault(name, (dirs, frames, delays, icons))
        index += dirs * frames
    _cache[dmi] = (w, h, states)
    return _cache[dmi]


def compose(*parts):
    """A single-frame state made by stacking the first frame of several source states."""
    base = None
    for dmi, name in parts:
        w, h, states = load(dmi)
        img = states[name][3][(0, 0)]
        base = img.copy() if base is None else Image.alpha_composite(base, img)
    return base


def write_rsi(name, mapping):
    dst = os.path.join(OUT, name + '.rsi')
    os.makedirs(dst, exist_ok=True)
    meta_states, size = [], None
    for out, src in mapping.items():
        if isinstance(src, Image.Image):
            img = src
            size = img.size
            img.save(os.path.join(dst, out + '.png'))
            meta_states.append({'name': out})
            continue
        dmi, state = src[0], src[1]
        first = src[2] if len(src) > 2 else None
        w, h, states = load(dmi)
        size = (w, h)
        dirs, frames, delays, icons = states[state]
        if first is not None:  # one frame only
            frames, delays = 1, [0.1]
            icons = {(d, 0): icons[(d, first)] for d in range(dirs)}
        ordered = [icons[(d, f)] for d in range(dirs) for f in range(frames)]
        n = len(ordered)
        c = math.ceil(math.sqrt(n))
        r = math.ceil(n / c)
        sheet = Image.new('RGBA', (c * w, r * h))
        for k, icon in enumerate(ordered):
            sheet.paste(icon, ((k % c) * w, (k // c) * h))
        sheet.save(os.path.join(dst, out + '.png'))
        entry = {'name': out}
        if dirs > 1:
            entry['directions'] = dirs
        if frames > 1:
            entry['delays'] = [list(delays) for _ in range(dirs)]
        meta_states.append(entry)
    meta = {'version': 1, 'license': 'CC-BY-SA-3.0', 'copyright': COPYRIGHT,
            'size': {'x': size[0], 'y': size[1]}, 'states': meta_states}
    with open(os.path.join(dst, 'meta.json'), 'w', newline='\n') as fh:
        json.dump(meta, fh, indent=4)
        fh.write('\n')


M = 'machines.dmi'
P = 'packed_machines.dmi'
SMALL = 'power_storage_unit/small_battery.dmi'
LARGE = 'power_storage_unit/large_battery.dmi'
CHEM = 'chemistry_machines.dmi'

RSIS = {
    'wind_turbine': {
        'turbine': ('wind_turbine.dmi', 'turbine'),
        'turbine_normal': ('wind_turbine.dmi', 'turbine_normal'),
        'turbine_storm': ('wind_turbine.dmi', 'turbine_storm'),
        'packed': ('wind_turbine.dmi', 'turbine_packed'),
    },
    'solar_string': {
        'solar': lambda: compose((M, 'sp_base'), (M, 'solar_panel_glass')),
        'packed': (P, 'solar_panel_packed'),
    },
    'battery_small': {
        'smes': (SMALL, 'smes'),
        'smes-og1': (SMALL, 'smes-og1'), 'smes-og2': (SMALL, 'smes-og2'), 'smes-og3': (SMALL, 'smes-og3'),
        'smes-og4': (SMALL, 'smes-og4'), 'smes-og5': (SMALL, 'smes-og5'),
        'smes-oc0': (SMALL, 'smes-oc0'), 'smes-oc1': (SMALL, 'smes-oc1'), 'smes-oc2': (SMALL, 'smes-oc1'),
        'smes-op0': (SMALL, 'smes-op0'), 'smes-op1': (SMALL, 'smes-op1'), 'smes-op2': (SMALL, 'smes-op1'),
        'packed': (P, 'battery_small_packed'),
    },
    'battery_large': {
        'smes': (LARGE, 'smes'),
        'smes-og1': (LARGE, 'smes-og1'), 'smes-og2': (LARGE, 'smes-og2'), 'smes-og3': (LARGE, 'smes-og3'),
        'smes-og4': (LARGE, 'smes-og4'), 'smes-og5': (LARGE, 'smes-og5'),
        'smes-oc0': (LARGE, 'smes-oc0'), 'smes-oc1': (LARGE, 'smes-oc1'), 'smes-oc2': (LARGE, 'smes-oc1'),
        'smes-op0': (LARGE, 'smes-op0'), 'smes-op1': (LARGE, 'smes-op1'), 'smes-op2': (LARGE, 'smes-op1'),
        'packed': (P, 'battery_large_packed'),
    },
    'rtg': {
        'rtg': (M, 'rtg'),
        'packed': (P, 'rtg_packed'),
    },
    'fuel_generator': {
        'idle': (M, 'fuel_generator_0'),
        'running': (M, 'fuel_generator_1'),
        'packed': (P, 'fuel_generator_packed'),
    },
    'fabricator': {
        'icon': (M, 'colony_lathe'),
        'building': (M, 'colony_lathe_working'),
        'packed': (P, 'colony_lathe_packed'),
    },
    'co2_cracker': {
        'idle': ('portable_machines.dmi', 'electrolyzer-off'),
        'working': ('portable_machines.dmi', 'electrolyzer-working'),
        'packed': ('parts_kits.dmi', 'co2_cracker'),
    },
    'heater': {
        'sheater-off': ('space_heater.dmi', 'sheater-off'),
        'sheater-standby': ('space_heater.dmi', 'sheater-standby'),
        'sheater-heat': ('space_heater.dmi', 'sheater-heat'),
        'sheater-cool': ('space_heater.dmi', 'sheater-cool'),
        'packed': (P, 'thermomachine_packed'),
    },
    'synthesizers': {
        'water': (CHEM, 'water_synth'),
        'nutrient': (CHEM, 'hydro_synth'),
        'dispenser': (CHEM, 'dispenser'),
        'water-packed': (CHEM, 'water_synth_parts'),
        'nutrient-packed': (CHEM, 'hydro_synth_parts'),
        'dispenser-packed': (CHEM, 'dispenser_parts'),
    },
    'rations_printer': {
        'icon': ('foodricator.dmi', 'biogenerator'),
        'building': lambda: compose(('foodricator.dmi', 'biogenerator'), ('foodricator.dmi', 'biogenerator_o_process')),
        'unlit': ('foodricator.dmi', 'biogenerator_o_screen'),
        'packed': ('foodricator.dmi', 'biogenerator_parts'),
    },
    'griddle': {
        'griddle': ('kitchen_stuff/griddle.dmi', 'griddletable_off'),
        'griddle-lit': ('kitchen_stuff/griddle.dmi', 'griddletable_on'),
        'packed': ('kitchen_stuff/griddle.dmi', 'griddle_parts'),
    },
    'range': {
        'range': ('kitchen_stuff/range.dmi', 'range_off'),
        'lid': ('kitchen_stuff/range.dmi', 'range_lid_closed'),
        'flame': ('kitchen_stuff/range.dmi', 'range_on_flame'),
        'glow': ('kitchen_stuff/range.dmi', 'range_on_overlay'),
        'packed': ('kitchen_stuff/range.dmi', 'range_packed'),
    },
    'recycler': {
        'grinder-o0': ('portable_machines.dmi', 'recycler'),
        'grinder-o1': ('portable_machines.dmi', 'recycler_grind'),
        'grinder-o2': ('portable_machines.dmi', 'recycler_grind'),
        'grinder-o0bld': ('portable_machines.dmi', 'recycler'),
        'grinder-o1bld': ('portable_machines.dmi', 'recycler_grind'),
        'grinder-o2bld': ('portable_machines.dmi', 'recycler_grind'),
        'packed': ('parts_kits.dmi', 'recycler'),
    },
    'ore_silo': {
        'silo': ('ore_silo.dmi', 'silo'),
        'silo_active': ('ore_silo.dmi', 'silo_active'),
        'packed': ('ore_silo.dmi', 'ore_silo'),
    },
}

if __name__ == '__main__':
  for rsi, mapping in RSIS.items():
    resolved = {k: (v() if callable(v) else v) for k, v in mapping.items()}
    write_rsi(rsi, resolved)
    print('wrote', rsi)
