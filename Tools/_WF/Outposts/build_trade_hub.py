"""The universal trade hub in the colony palette: a floor-level drop pad, its unshaded beacon lights and its pack.
Run: python Tools/_WF/Outposts/build_trade_hub.py Resources/Textures/_WF/Outposts/Colony/trade_hub.rsi
"""
import json, math, os, sys
from PIL import Image

OUT = sys.argv[1]
PAL = {'0': (18, 19, 23), '1': (34, 35, 42), '2': (53, 56, 65), '3': (73, 71, 92), '4': (86, 90, 99),
       '5': (119, 123, 131), '6': (138, 138, 158), '7': (194, 193, 201), '8': (235, 235, 235),
       'd': (84, 56, 26), 'o': (138, 92, 36), 'y': (232, 163, 61), 'w': (255, 214, 140)}
# The pad's top face and front edge.
X0, X1, Y0, YT, YF = 3, 28, 7, 22, 25
CX, CY = (X0 + X1 + 1) / 2, (Y0 + YT + 1) / 2
LAMPS = [(X0 + 2, Y0 + 2), (X1 - 3, Y0 + 2), (X1 - 3, YT - 3), (X0 + 2, YT - 3)]


def image(px):
    img = Image.new('RGBA', (32, 32))
    for (x, y), c in px.items():
        img.putpixel((x, y), PAL[c] + (255,) if isinstance(c, str) else c)
    return img


def ring(x, y):
    """Distance from the target's centre, in units of its radius, squashed for the 3/4 view."""
    return math.hypot((x + 0.5 - CX) / 6.6, (y + 0.5 - CY) / 4.6)


def pad():
    """The drop pad: a raised grey rim, a dashed amber landing box, a white target ring and four beacon domes."""
    px = {}
    for y in range(Y0, YF + 1):
        for x in range(X0, X1 + 1):
            if x in (X0, X1) and y in (Y0, YF):
                continue
            if y <= YT:
                if x in (X0, X1) or y == Y0:
                    c = '1'
                elif y == Y0 + 1:
                    c = '8' if X0 + 2 <= x <= X1 - 2 else '7'
                elif x == X0 + 1:
                    c = '6'
                elif x == X1 - 1:
                    c = '5'
                elif y == YT:
                    c = '2'
                elif y == Y0 + 2:
                    c = '5'
                elif x in (X0 + 7, X1 - 7):
                    c = '3'
                else:
                    c = '4'
            else:
                if x in (X0, X1) or y == YF:
                    c = '1'
                elif y == YT + 1:
                    c = '5'
                elif X0 + 9 <= x <= X1 - 9:
                    c = '1' if x % 2 == 0 else '2'
                else:
                    c = '2'
            px[(x, y)] = c
    # the shadow it casts on the floor
    for x in range(X0 + 1, X1 + 1):
        px[(x, YF + 1)] = '0'

    # the landing box, amber dashes two on and two off, a row in from the rim
    bx0, bx1, by0, by1 = X0 + 3, X1 - 3, Y0 + 3, YT - 2
    for y in range(by0, by1 + 1):
        for x in range(bx0, bx1 + 1):
            if (x in (bx0, bx1) or y in (by0, by1)) and (x + y) // 2 % 2 == 0:
                px[(x, y)] = 'y'
            elif (x in (bx0, bx1) or y in (by0, by1)):
                px[(x, y)] = 'd' if y == by1 or x == bx1 else '2'

    # target ring with a dark recessed disc and a cross in it
    for y in range(by0 + 1, by1):
        for x in range(bx0 + 1, bx1):
            d = ring(x, y)
            if 0.8 < d <= 1.0:
                px[(x, y)] = '8' if y < CY - 2 else '7' if y < CY + 1 else '6'
            elif d <= 0.8:
                px[(x, y)] = '1' if y < CY - 2 and d > 0.5 else '2'
    cx, cy = int(CX), int(CY)
    for i in range(-2, 2):
        px[(cx + i, cy)] = '6'
        px[(cx, cy + i)] = '6' if i < 0 else '5'
    px[(cx, cy)] = '7'

    # beacon domes on the box corners: an outlined cap, lit from the top left
    for lx, ly in LAMPS:
        for dx in range(-1, 3):
            for dy in range(-1, 3):
                if dx in (-1, 2) and dy in (-1, 2):
                    continue
                edge = dx in (-1, 2) or dy in (-1, 2)
                px[(lx + dx, ly + dy)] = '1' if edge else '6' if (dx, dy) == (0, 0) else '3' if dy == 0 else '2'
    return px


def lights(frame):
    """Beacons chasing round the pad, the lit one bright with the last one fading, each lit cap glowing round it."""
    px = {}
    for i, (lx, ly) in enumerate(LAMPS):
        age = (frame - i) % 4
        if age > 1:
            continue
        hot = age == 0
        for dx in range(-2, 4):
            for dy in range(-2, 4):
                if 0 <= dx <= 1 and 0 <= dy <= 1:
                    c = ('w' if (dx, dy) == (0, 0) else 'y') if hot else ('y' if (dx, dy) == (0, 0) else 'o')
                    px[(lx + dx, ly + dy)] = PAL[c] + (255,)
                elif hot and -1 <= dx <= 2 and -1 <= dy <= 2 and not (dx in (-1, 2) and dy in (-1, 2)):
                    px[(lx + dx, ly + dy)] = PAL['y'] + (110,)
                elif hot and (dx in (0, 1) and dy in (-2, 3) or dy in (0, 1) and dx in (-2, 3)):
                    px[(lx + dx, ly + dy)] = PAL['y'] + (45,)
    return px


def packed():
    """The pad folded for carrying: two hinged deck leaves with the hazard box on their faces, on a white frame."""
    px = {}
    # back leaf, then front leaf, each 10 wide and 15 tall, standing on the frame
    for left, top, light in ((9, 5, False), (14, 7, True)):
        w, h = 11, 17
        for y in range(top, top + h):
            for x in range(left, left + w):
                if x in (left, left + w - 1) or y in (top, top + h - 1):
                    c = '1'
                elif y == top + 1:
                    c = '7' if light else '6'
                elif x == left + 1:
                    c = '6' if light else '5'
                elif x == left + w - 2 or y == top + h - 2:
                    c = '2'
                else:
                    c = '4' if light else '3'
                    if light and (x in (left + 3, left + w - 4) and top + 3 <= y <= top + h - 4
                                  or y in (top + 3, top + h - 4) and left + 3 <= x <= left + w - 4):
                        c = 'y' if (x + y) // 2 % 2 == 0 else '4'
                px[(x, y)] = c
    # a target mark on the front leaf, and the hinge between the leaves
    for x, y in ((19, 14), (19, 15), (18, 15), (20, 15), (19, 16)):
        px[(x, y)] = '7'
    for y in range(9, 23, 3):
        px[(14, y)] = '6'
    # the carrying frame: white rails under and beside the leaves, as the other packs have
    for x in range(7, 27):
        px[(x, 24)] = '8' if 8 <= x <= 25 else '7'
        px[(x, 25)] = '6'
    for y in range(12, 24):
        px[(26, y)] = '7'
        px[(27, y)] = '6' if y < 23 else '1'
    px[(26, 11)] = '6'
    for x in range(7, 28):
        px.setdefault((x, 26), '1')
    return px


os.makedirs(OUT, exist_ok=True)
image(pad()).save(f'{OUT}/base.png')
sheet = Image.new('RGBA', (64, 64))
for f in range(4):
    sheet.paste(image(lights(f)), (f % 2 * 32, f // 2 * 32))
sheet.save(f'{OUT}/lights.png')
image(packed()).save(f'{OUT}/packed.png')
with open(f'{OUT}/meta.json', 'w', newline='\n') as f:
    json.dump({'version': 1, 'license': 'CC-BY-SA-3.0',
               'copyright': "Made for Wolfgate in the style and palette of NovaSector's colony fabricator machines "
                            "(CC BY-SA 3.0)",
               'size': {'x': 32, 'y': 32},
               'states': [{'name': 'base'}, {'name': 'lights', 'delays': [[0.3, 0.3, 0.3, 0.3]]},
                          {'name': 'packed'}]}, f, indent=4)
    f.write('\n')
