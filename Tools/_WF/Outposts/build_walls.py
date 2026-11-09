"""Colony prefab walls, windows, doors, shutters, kits and the arc welder, from NovaSector DMIs.

Nova's walls and doors are drawn for tg's lit-top view, so they are adapted to our 3/4 style: the prefab wall and
window take the stock solid wall's and window's corner geometry, recoloured to Nova's palette, and the doors' lit lids
(rows 1-3 of each frame) are painted as our dark wall tops.
Run: python Tools/_WF/Outposts/build_walls.py <NovaSector colony_fabricator/icons dir> Resources/Textures/_WF/Outposts/Colony
"""
import json, math, os, sys
from PIL import Image
import build_colony as bc

bc.ICONS, bc.OUT = sys.argv[1], sys.argv[2]
TEXTURES = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'Resources', 'Textures')
NAVY, DUSK, MID, LIGHT, BEVEL, WHITE = (30, 30, 50), (73, 71, 92), (138, 138, 158), (194, 193, 201), (216, 215, 223), (235, 235, 235)
# stock solid wall greys -> Nova's
WALL = {(26, 26, 29): NAVY, (27, 27, 29): NAVY, (51, 57, 65): MID, (81, 81, 81): LIGHT, (123, 123, 123): BEVEL,
        (163, 163, 163): WHITE}
# stock window glass -> lavender panes and light plastic rims
GLASS = {(91, 152, 183, 128): (122, 128, 168, 128), (72, 118, 170, 129): (96, 100, 146, 129),
         (162, 214, 218, 149): (206, 205, 216, 170), (48, 65, 128, 190): (58, 58, 92, 200),
         (194, 230, 233, 213): (220, 219, 228, 220), (235, 247, 255, 219): (240, 240, 245, 230)}
# a door's lit lid -> our dark wall top
LID = {WHITE: NAVY, LIGHT: DUSK, MID: DUSK}
STOCK_COPYRIGHT = ('Geometry from the stock solid wall and window (rye-rice and 20nypercent, modified by AftrLite), '
                   "recoloured to NovaSector's colony fabricator palette for Wolfgate")


def recolour(src, name, base_from, table, rgba=False):
    """Copies a stock corner-smoothed RSI's full and {base}0..7 states, recoloured, as {name}.rsi with base 'prefab'."""
    src = os.path.join(TEXTURES, src)
    dst = os.path.join(bc.OUT, name + '.rsi')
    os.makedirs(dst, exist_ok=True)
    meta = json.load(open(os.path.join(src, 'meta.json')))
    states = []
    for s in meta['states']:
        n = s['name']
        if n != 'full' and not (n.startswith(base_from) and n[len(base_from):].isdigit()):
            continue
        im = Image.open(os.path.join(src, n + '.png')).convert('RGBA')
        px = im.load()
        for y in range(im.height):
            for x in range(im.width):
                c = px[x, y]
                if c[3]:
                    px[x, y] = table[c] if rgba else table[c[:3]] + (c[3],)
        out = 'full' if n == 'full' else 'prefab' + n[len(base_from):]
        im.save(os.path.join(dst, out + '.png'))
        states.append(dict(s, name=out))
    write_meta(dst, states, STOCK_COPYRIGHT)


def lidded(img):
    """A door frame with its lit lid painted as our dark wall top."""
    img = img.copy()
    px = img.load()
    for y in range(1, 4):
        for x in range(img.width):
            c = px[x, y]
            if c[3] and c[:3] in LID:
                px[x, y] = LID[c[:3]] + (c[3],)
    return img


def write_meta(dst, states, copyright=None):
    with open(os.path.join(dst, 'meta.json'), 'w', newline='\n') as fh:
        json.dump({'version': 1, 'license': 'CC-BY-SA-3.0', 'copyright': copyright or bc.COPYRIGHT,
                   'size': {'x': 32, 'y': 32}, 'states': states}, fh, indent=4)
        fh.write('\n')


def anim(dmi, state, total=None, lid=False):
    """(frames, delays) of a one-direction state, its delays scaled to `total` seconds."""
    _, _, states = bc.load(dmi)
    dirs, frames, delays, icons = states[state]
    if total:
        scale = total / sum(delays)
        delays = [round(d * scale, 3) for d in delays]
    out = [icons[(0, f)] for f in range(frames)]
    return [lidded(f) for f in out] if lid else out, delays


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


def first(dmi, state, lid=False):
    img = bc.load(dmi)[2][state][3][(0, 0)]
    return lidded(img) if lid else img


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


recolour('Structures/Walls/solid.rsi', 'prefab_wall', 'solid', WALL)
recolour('Structures/Windows/window.rsi', 'prefab_window', 'window', GLASS, rgba=True)

A, M, SH, PK, OV = 'doors/airlock.dmi', 'doors/airlock_manual.dmi', 'doors/shutter.dmi', 'doors/packed.dmi', 'doors/overlays.dmi'
write_states('airlock', {
    'closed': first(A, 'closed', True),
    'open': first(A, 'open', True),
    'opening': anim(A, 'opening', 0.8, True),
    'closing': anim(A, 'closing', 0.8, True),
    'assembly': first(A, 'construction', True),
    'packed': first(PK, 'airlock_parts'),
})
write_states('door', {
    'closed': first(M, 'manual', True),
    'open': first(M, 'manualopen', True),
    'opening': anim(M, 'manualopening', 1.2, True),
    'closing': anim(M, 'manualclosing', 1.2, True),
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

FLOORS = ['colony_grey', 'colony_grey_texture', 'colony_grey_bolts', 'colony_white', 'colony_white_texture', 'colony_white_bolts']
write_states('floors', {f: first('tiles.dmi', f) for f in FLOORS})
write_states('floor_tiles', {f: first('tiles_item.dmi', f) for f in FLOORS})
write_states('plastic_panels', {'sheet': first('tiles_item.dmi', 'sheet-plastic_3')})
print('done')
