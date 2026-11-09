"""Outpost foundation plates and roof panels, three stack sizes each, in the colony palette.
Run: python Tools/_WF/Outposts/build_plates.py Resources/Textures/_WF/Outposts/building.rsi
"""
import json, os, sys
from PIL import Image

OUT = sys.argv[1]
PAL = {'0': (34, 35, 42), '1': (53, 56, 65), '2': (73, 71, 92), '4': (86, 90, 99), '6': (119, 123, 131),
       '7': (138, 138, 158), 'h': (166, 165, 178), '9': (194, 193, 201), 'k': (214, 213, 220), 'a': (235, 235, 235),
       'y': (232, 163, 61), 'd': (138, 92, 36)}
W, X0 = 18, 7


def plate(px, top):
    """A heavy colony-grey plate: a raised inner border, corner bolts, and hazard striping on its front edge."""
    H, E = 12, 4
    for y in range(H + E):
        for x in range(W):
            edge = x in (0, W - 1)
            if y < H:
                if y == 0 and edge:
                    continue
                if edge or y == 0:
                    c = '0'
                elif y == 1:
                    c = '6'
                elif y == H - 1:
                    c = '6'
                elif (x, y) in ((2, 2), (W - 3, 2), (2, H - 3), (W - 3, H - 3)):
                    c = '0'
                elif 4 <= x <= W - 5 and y in (3,) or x == 4 and 3 <= y <= H - 4:
                    c = '2'
                elif 4 <= x <= W - 5 and y == H - 4 or x == W - 5 and 3 <= y <= H - 4:
                    c = '6'
                else:
                    c = '4'
            else:
                if y == H + E - 1 and edge:
                    continue
                if edge or y == H + E - 1:
                    c = '0'
                elif y == H:
                    c = '2'
                else:
                    c = 'y' if (x + y) // 2 % 2 else '1'
            px[(X0 + x, top + y)] = c


def panel(px, top):
    """A light corrugated roof panel: ribs along it, the profile showing on its front edge."""
    H, E = 12, 3
    for y in range(H + E):
        for x in range(W):
            edge = x in (0, W - 1)
            if (y == 0 or y == H + E - 1) and edge:
                continue
            if edge or y == 0 or y == H + E - 1:
                c = '7'
            elif y < H:
                c = {1: 'a', 3: 'h'}.get(x % 4, '9')
                if y == 1:
                    c = 'a' if c != 'h' else 'k'
                elif y == H - 1:
                    c = 'h' if c != 'a' else '9'
            else:
                c = {1: 'k', 3: '7'}.get(x % 4, 'h') if y == H else {1: 'h', 3: '2'}.get(x % 4, '7')
            px[(X0 + x, top + y)] = c


def stack(draw, n, h):
    px = {}
    bottom = 25
    # lowest plate first, each one above it 4 px higher
    for i in range(n):
        draw(px, bottom - h + 1 - i * 4)
    img = Image.new('RGBA', (32, 32))
    for (x, y), c in px.items():
        img.putpixel((x, y), PAL[c] + (255,))
    return img


os.makedirs(OUT, exist_ok=True)
states = []
for name, draw, h in (('plate', plate, 16), ('panel', panel, 15)):
    for n in (1, 2, 3):
        st = name if n == 1 else f'{name}_{n}'
        stack(draw, n, h).save(f'{OUT}/{st}.png')
        states.append({'name': st})
with open(f'{OUT}/meta.json', 'w', newline='\n') as f:
    json.dump({'version': 1, 'license': 'CC-BY-SA-3.0',
               'copyright': "Made for Wolfgate in the palette of NovaSector's colony fabricator set (CC BY-SA 3.0)",
               'size': {'x': 32, 'y': 32}, 'states': states}, f, indent=4)
    f.write('\n')
