"""Draws the halo launcher's shield bubble and amber charge orb.

Run from the repository root:

    python Tools/_WG/Augments/build_halo_sprites.py

The bubble is a hex mesh inside a rim, with a bright band sweeping round the edge; the orb is a hot core in a
stepped amber glow that flickers. Both are drawn per pixel with stepped alpha, no anti-aliasing.
"""
import json
import math
import os

from PIL import Image

OUTPUT = "Resources/Textures/_WG/Augments"
COPYRIGHT = "Drawn for Wolfgate by Tools/_WG/Augments/build_halo_sprites.py"

RIM = (255, 226, 84)
RIM2 = (255, 190, 50)
FILL = (255, 214, 72)
HI = (255, 250, 205)
CORE = (255, 250, 220)
HOT = (255, 214, 96)
AMBER = (255, 160, 32)
DEEP = (214, 104, 16)

# One pointy-top hex outline round its centre, with 2:1 diagonals so the edges step cleanly.
HEX = [(0, -4), (1, -4), (2, -3), (3, -3), (4, -2), (4, -1), (4, 0), (4, 1), (4, 2), (3, 3), (2, 3), (1, 4),
       (0, 4), (-1, 4), (-2, 3), (-3, 3), (-4, 2), (-4, 1), (-4, 0), (-4, -1), (-4, -2), (-3, -3), (-2, -3),
       (-1, -4)]


def hex_mesh(centre):
    pts = set()
    for j in range(-8, 9):
        for i in range(-8, 9):
            cx = centre + 8 * i + 4 * (j % 2)
            cy = centre + 6 * j
            pts.update((cx + x, cy + y) for x, y in HEX)
    return pts


def shield_frame(f, frames, size=64, radius=17):
    centre = size / 2 - 0.5
    mesh = hex_mesh(size // 2 - 1)
    im = Image.new("RGBA", (size, size))
    px = im.load()
    sweep = f * 360 / frames
    pulse = [34, 40, 46, 40, 34, 30, 28, 30][f]
    for y in range(size):
        for x in range(size):
            dx, dy = x - centre, y - centre
            r = math.hypot(dx, dy)
            ri = math.floor(r)
            if ri > radius:
                continue
            ang = math.degrees(math.atan2(-dy, dx)) % 360
            d = min(abs(ang - sweep), 360 - abs(ang - sweep))
            if ri == radius:
                col, a = RIM, 215
                if 115 <= ang <= 165:
                    col, a = HI, 245
                elif d < 22:
                    col, a = HI, 235
            elif ri == radius - 1:
                col, a = (RIM, 190) if d < 22 else (RIM2, 140)
            elif (x, y) in mesh:
                # The mesh fades in toward the rim; the sweep lights its outer half.
                col, a = RIM, int(pulse + 20 + 90 * (r / radius) ** 2)
                if d < 30 and r > radius * 0.55:
                    a = min(a + 80, 235)
            elif ri >= radius - 3:
                col, a = FILL, pulse + 34
            else:
                col, a = FILL, pulse
            px[x, y] = (*col, a)
    for x, y in [(22, 21), (23, 21), (21, 22), (22, 22), (21, 23)]:
        px[x, y] = (*HI, 200)
    return im


def orb_frame(f, size=32):
    centre = size / 2 - 0.5
    grow = [0, 0.5, 1, 0.5][f]
    im = Image.new("RGBA", (size, size))
    px = im.load()
    for y in range(size):
        for x in range(size):
            r = math.hypot(x - centre, y - centre)
            if r < 1.6:
                c = (*CORE, 255)
            elif r < 2.8:
                c = (*HOT, 255)
            elif r < 3.9:
                c = (*AMBER, 255)
            elif r < 4.6:
                c = (*DEEP, 255)
            elif r < 5.6 + grow:
                c = (*AMBER, 110)
            elif r < 6.8 + grow:
                c = (*AMBER, 50)
            else:
                continue
            px[x, y] = c
    px[14, 13] = (*CORE, 255)
    return im


def write_rsi(name, state, frames, size, delay, columns):
    path = os.path.join(OUTPUT, name)
    os.makedirs(path, exist_ok=True)
    rows = math.ceil(len(frames) / columns)
    sheet = Image.new("RGBA", (size * columns, size * rows))
    for i, frame in enumerate(frames):
        sheet.paste(frame, ((i % columns) * size, (i // columns) * size))
    sheet.save(os.path.join(path, f"{state}.png"))
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": COPYRIGHT,
        "size": {"x": size, "y": size},
        "states": [{"name": state, "delays": [[delay] * len(frames)]}],
    }
    with open(os.path.join(path, "meta.json"), "w", newline="\n") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")


def main():
    write_rsi("halo_shield.rsi", "shield", [shield_frame(f, 8) for f in range(8)], 64, 0.08, 4)
    write_rsi("halo_orb.rsi", "orb", [orb_frame(f) for f in range(4)], 32, 0.1, 2)


if __name__ == "__main__":
    main()
