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
# a side-view door's long edges, outermost first
EDGE = (NAVY, DUSK, MID)
# the airlock's status screen (x, y, w, h on the closed frame) and what it shows; Nova's airlock has no motion lights
SCREEN = (22, 12, 7, 7)
LIGHTS = {'G': (119, 189, 93), 'g': (39, 137, 93), 'Y': (255, 214, 140), 'y': (232, 163, 61),
          'R': (248, 77, 57), 'r': (215, 32, 23), 'W': WHITE,
          'q': (170, 40, 30), 'u': (200, 150, 50), 'v': (60, 130, 180), 'k': (13, 12, 25)}
GLYPH_OPEN = ['.......', '..G.G..', '.G...G.', 'G..g..G', '.G...G.', '..G.G..', '.......']
GLYPH_CLOSE = ['.......', '..yyy..', '.yYYYy.', '.yYYYy.', '.yYYYy.', '..yyy..', '.......']
GLYPH_DENY = ['.......', '.rrrrr.', 'rrrrrrr', 'rWWWWWr', 'rrrrrrr', '.rrrrr.', '.......']
GLYPH_SPARKS = ['..W....', '.Y..Y..', '...W..Y', 'Y.Y....', '..W.Y..', '.Y....W', '...Y...']
# the maintenance panel open: the screen lifted off its wiring
GLYPH_WIRES = ['kqkukvk', 'kqkukvk', 'kkqukvk', 'kkqkuvk', 'kqkkuvk', 'kqkukvk', 'kqkukvk']
DOOR_COPYRIGHT = bc.COPYRIGHT + '; side views and the airlock screen and panel overlays drawn from it for Wolfgate'
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


def write_states(name, mapping, copyright=None):
    """mapping: out state -> image, (frames, delays), or Dirs."""
    dst = os.path.join(bc.OUT, name + '.rsi')
    os.makedirs(dst, exist_ok=True)
    meta = []
    for out, src in mapping.items():
        if isinstance(src, Dirs):
            frames, delays, extra = [f for d in src.frames for f in d], src.delays, {'directions': 4}
        else:
            frames, delays = (src, None) if isinstance(src, Image.Image) else src
            extra = {}
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
        state = {'name': out, **extra}
        if delays:
            state['delays'] = [delays] * (4 if extra else 1)
        meta.append(state)
    write_meta(dst, meta, copyright)


class Dirs:
    """A door state in four directions: the front view facing north and south, the side view east and west."""

    def __init__(self, src, overlay=False, sym=False, screen=False):
        frames, self.delays = ([src], None) if isinstance(src, Image.Image) else src
        sides = [side(upright(f) if screen else f, overlay, sym) for f in frames]
        self.frames = [frames, frames, sides, sides]


def side(img, overlay=False, sym=False):
    """A front-view door frame turned to stand in a north-south wall: the face transposed so its leaves slide north and
    south, the lid and foot replaced by even frame edges. An overlay keeps its edge pixels; sym mirrors it across."""
    out = Image.new('RGBA', img.size)
    src, px = img.load(), out.load()
    for y in range(32):
        for x in range(3, 29):
            px[x, y] = src[y, x + 1]
        if overlay:
            for x, r in ((0, 1), (1, 2), (2, 3), (29, 30), (30, 31), (31, 31)):
                px[x, y] = src[y, r]
        else:
            for xs, r in (((0, 1, 2), 4), ((31, 30, 29), 29)):
                if src[y, r][3]:
                    for x, c in zip(xs, EDGE):
                        px[x, y] = c + (255,)
    if sym:
        out.alpha_composite(out.transpose(Image.FLIP_LEFT_RIGHT))
    return out


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


def upright(img):
    """The overlay with its screen box pre-turned, so what the screen shows reads upright on the side view."""
    x, y, w, h = SCREEN
    img = img.copy()
    img.paste(img.crop((x, y, x + w, y + h)).transpose(Image.TRANSPOSE), (x, y))
    return img


def screen_offset(closed, frame):
    """How far the airlock's screen has slid right in this frame, or None once it is off the door."""
    x0, y0, w, h = SCREEN
    cp, fp = closed.load(), frame.load()
    for dx in range(32 - x0):
        cols = [x for x in range(x0, x0 + w) if x + dx < 32]
        if cols and all(fp[x + dx, y] == cp[x, y] for x in cols for y in range(y0, y0 + h)):
            return dx
    return None


def screen_lights(closed, frames, glyph):
    """Unlit frames that show the glyph on the airlock's screen, riding along with its leaf."""
    out = []
    for f in frames:
        img = Image.new('RGBA', (32, 32))
        dx = screen_offset(closed, f)
        for r, line in enumerate(glyph if dx is not None else []):
            for c, ch in enumerate(line):
                x = SCREEN[0] + c + dx
                if ch in LIGHTS and x < 32:
                    img.putpixel((x, SCREEN[1] + r), LIGHTS[ch] + (255,))
        out.append(img)
    return out


recolour('Structures/Walls/solid.rsi', 'prefab_wall', 'solid', WALL)
recolour('Structures/Windows/window.rsi', 'prefab_window', 'window', GLASS, rgba=True)

A, M, SH, PK, OV = 'doors/airlock.dmi', 'doors/airlock_manual.dmi', 'doors/shutter.dmi', 'doors/packed.dmi', 'doors/overlays.dmi'
air_closed = first(A, 'closed', True)
air_opening, air_closing = anim(A, 'opening', 0.8, True), anim(A, 'closing', 0.8, True)
blank = Image.new('RGBA', (32, 32))
deny = screen_lights(air_closed, [air_closed], GLYPH_DENY)[0]
write_states('airlock', {
    'closed': Dirs(air_closed),
    'open': Dirs(first(A, 'open', True)),
    'opening': Dirs(air_opening),
    'closing': Dirs(air_closing),
    'assembly': Dirs(first(A, 'construction', True)),
    'packed': first(PK, 'airlock_parts'),
    'closed_unlit': Dirs(blank, True),
    'open_unlit': Dirs(blank, True),
    'opening_unlit': Dirs((screen_lights(air_closed, air_opening[0], GLYPH_OPEN), air_opening[1]), True),
    'closing_unlit': Dirs((screen_lights(air_closed, air_closing[0], GLYPH_CLOSE), air_closing[1]), True),
    'deny_unlit': Dirs(([deny, blank, deny, blank], [0.1, 0.1, 0.1, 0.1]), True, screen=True),
    'bolted_unlit': Dirs(first(OV, 'lights_bolts'), True, sym=True),
    'emergency_unlit': Dirs(anim(OV, 'lights_emergency'), True, screen=True),
    'welded': Dirs(first(OV, 'welded'), True),
    'sparks': Dirs(screen_lights(air_closed, [air_closed], GLYPH_SPARKS)[0], True, screen=True),
    'panel_open': Dirs(screen_lights(air_closed, [air_closed], GLYPH_WIRES)[0], True),
    'panel_opening': Dirs((screen_lights(air_closed, air_opening[0], GLYPH_WIRES), air_opening[1]), True),
    'panel_closing': Dirs((screen_lights(air_closed, air_closing[0], GLYPH_WIRES), air_closing[1]), True),
}, DOOR_COPYRIGHT)
write_states('door', {
    'closed': Dirs(first(M, 'manual', True)),
    'open': Dirs(first(M, 'manualopen', True)),
    'opening': Dirs(anim(M, 'manualopening', 1.2, True)),
    'closing': Dirs(anim(M, 'manualclosing', 1.2, True)),
    'packed': first(PK, 'airlock_parts_manual'),
}, DOOR_COPYRIGHT)
write_states('shutters', {
    'closed': Dirs(first(SH, 'closed')),
    'open': Dirs(first(SH, 'open')),
    'opening': Dirs(anim(SH, 'opening', 1.4)),
    'closing': Dirs(anim(SH, 'closing', 1.4)),
    'assembly': Dirs(first(SH, 'open')),
    'welded': Dirs(first(OV, 'welded'), True),
    'packed': first(PK, 'shutters_parts'),
}, DOOR_COPYRIGHT)
write_states('arc_welder', {
    'icon': first('tools.dmi', 'arc_welder'),
    'welder_flame': overlay_diff('tools.dmi', 'arc_welder', 'arc_welder-on'),
})

FLOORS = ['colony_grey', 'colony_grey_texture', 'colony_grey_bolts', 'colony_white', 'colony_white_texture', 'colony_white_bolts']
write_states('floors', {f: first('tiles.dmi', f) for f in FLOORS})
write_states('floor_tiles', {f: first('tiles_item.dmi', f) for f in FLOORS})
write_states('plastic_panels', {'sheet': first('tiles_item.dmi', 'sheet-plastic_3')})
print('done')
