"""Colony prefab walls, windows, doors, shutters, kits and the arc welder, from NovaSector DMIs.

tg bitmask walls (prefab-N, N=1,S=2,E=4,W=8,NE=16,SE=32,SW=64,NW=128) become SS14 corner states:
{base}0..7 with 4 directions, each direction holding one quadrant (S=SE, N=NW, E=NE, W=SW), where the
state's bits are CCW=1, Diagonal=2, CW=4 for that corner.
Run: python Tools/_WF/Outposts/build_walls.py <NovaSector colony_fabricator/icons dir> Resources/Textures/_WF/Outposts/Colony
"""
import json, math, os, sys
from PIL import Image
import build_colony as bc

bc.ICONS, bc.OUT = sys.argv[1], sys.argv[2]
N, S, E, W, NE, SE, SW, NW = 1, 2, 4, 8, 16, 32, 64, 128

# corner: (ccw bit, diagonal bit, cw bit, quadrant box in a 32x32 icon)
CORNERS = {
    'SE': (E, SE, S, (16, 16, 32, 32)),
    'NW': (W, NW, N, (0, 0, 16, 16)),
    'NE': (N, NE, E, (16, 0, 32, 16)),
    'SW': (S, SW, W, (0, 16, 16, 32)),
}
ORDER = ['SE', 'NW', 'NE', 'SW']  # RSI directions S, N, E, W


def corner_states(dmi, prefix, base):
    _, _, states = bc.load(dmi)
    out = {'full': states[f'{prefix}0'][3][(0, 0)]}
    for fill in range(8):
        sheet = Image.new('RGBA', (64, 64))
        for k, corner in enumerate(ORDER):
            ccw, diag, cw, box = CORNERS[corner]
            j = (ccw if fill & 1 else 0) | (cw if fill & 4 else 0)
            if fill & 2 and fill & 1 and fill & 4:
                j |= diag
            icon = states[f'{prefix}{j}'][3][(0, 0)]
            frame = Image.new('RGBA', (32, 32))
            frame.paste(icon.crop(box), box[:2])
            sheet.paste(frame, ((k % 2) * 32, (k // 2) * 32))
        out[f'{base}{fill}'] = sheet
    return out


def write_corner_rsi(name, states):
    dst = os.path.join(bc.OUT, name + '.rsi')
    os.makedirs(dst, exist_ok=True)
    meta = []
    for k, img in states.items():
        img.save(os.path.join(dst, k + '.png'))
        meta.append({'name': k} if k == 'full' else {'name': k, 'directions': 4})
    write_meta(dst, meta)


def write_meta(dst, states):
    with open(os.path.join(dst, 'meta.json'), 'w', newline='\n') as fh:
        json.dump({'version': 1, 'license': 'CC-BY-SA-3.0', 'copyright': bc.COPYRIGHT,
                   'size': {'x': 32, 'y': 32}, 'states': states}, fh, indent=4)
        fh.write('\n')


def anim(dmi, state, total=None):
    """(frames, delays) of a one-direction state, its delays scaled to `total` seconds."""
    _, _, states = bc.load(dmi)
    dirs, frames, delays, icons = states[state]
    if total:
        scale = total / sum(delays)
        delays = [round(d * scale, 3) for d in delays]
    return [icons[(0, f)] for f in range(frames)], delays


def write_states(name, mapping):
    """mapping: out state -> image, or (frames, delays)."""
    dst = os.path.join(bc.OUT, name + '.rsi')
    os.makedirs(dst, exist_ok=True)
    meta = []
    for out, src in mapping.items():
        frames, delays = (src, None) if isinstance(src, Image.Image) else src
        if isinstance(frames, Image.Image):
            frames.save(os.path.join(dst, out + '.png'))
            meta.append({'name': out})
            continue
        c = math.ceil(math.sqrt(len(frames)))
        r = math.ceil(len(frames) / c)
        sheet = Image.new('RGBA', (c * 32, r * 32))
        for k, f in enumerate(frames):
            sheet.paste(f, ((k % c) * 32, (k // c) * 32))
        sheet.save(os.path.join(dst, out + '.png'))
        meta.append({'name': out, 'delays': [delays]})
    write_meta(dst, meta)


def first(dmi, state):
    return bc.load(dmi)[2][state][3][(0, 0)]


def overlay_diff(dmi, base_state, lit_state):
    """The lit state's frames with only the pixels that differ from the base state."""
    base = first(dmi, base_state)
    frames, delays = anim(dmi, lit_state)
    out = []
    for f in frames:
        diff = Image.new('RGBA', f.size)
        bp, fp, dp = base.load(), f.load(), diff.load()
        for y in range(f.height):
            for x in range(f.width):
                if fp[x, y] != bp[x, y] and fp[x, y][3]:
                    dp[x, y] = fp[x, y]
        out.append(diff)
    return out, delays


write_corner_rsi('prefab_wall', corner_states('prefab_wall.dmi', 'prefab-', 'prefab'))
write_corner_rsi('prefab_window', corner_states('prefab_window.dmi', 'prefab-', 'prefab'))

A, M, SH, PK, OV = 'doors/airlock.dmi', 'doors/airlock_manual.dmi', 'doors/shutter.dmi', 'doors/packed.dmi', 'doors/overlays.dmi'
write_states('airlock', {
    'closed': first(A, 'closed'),
    'open': first(A, 'open'),
    'opening': anim(A, 'opening', 0.8),
    'closing': anim(A, 'closing', 0.8),
    'assembly': first(A, 'construction'),
    'packed': first(PK, 'airlock_parts'),
})
write_states('door', {
    'closed': first(M, 'manual'),
    'open': first(M, 'manualopen'),
    'opening': anim(M, 'manualopening', 1.2),
    'closing': anim(M, 'manualclosing', 1.2),
    'packed': first(PK, 'airlock_parts_manual'),
})
write_states('shutters', {
    'closed': first(SH, 'closed'),
    'open': first(SH, 'open'),
    'opening': anim(SH, 'opening', 1.4),
    'closing': anim(SH, 'closing', 1.4),
    'assembly': first(SH, 'open'),
    'welded': first(OV, 'welded'),
    'packed': first(PK, 'shutters_parts'),
})
write_states('arc_welder', {
    'icon': first('tools.dmi', 'arc_welder'),
    'welder_flame': overlay_diff('tools.dmi', 'arc_welder', 'arc_welder-on'),
})
print('done')

FLOORS = ['colony_grey', 'colony_grey_texture', 'colony_grey_bolts', 'colony_white', 'colony_white_texture', 'colony_white_bolts']
write_states('floors', {f: first('tiles.dmi', f) for f in FLOORS})
write_states('floor_tiles', {f: first('tiles_item.dmi', f) for f in FLOORS})
write_states('plastic_panels', {'sheet': first('tiles_item.dmi', 'sheet-plastic_3')})
print('floors')
