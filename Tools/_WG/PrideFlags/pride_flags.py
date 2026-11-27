"""Draws the pride flags in Resources/Textures/_WG/PrideFlags/flags.rsi from the rainbow wall flag.

The rainbow flag (Structures/Wallmounts/flags.rsi, lgbtq_flag) is a draped banner: six stripes that sag between two
nails, each shaded darker towards its edges. Every cloth pixel is read as a place on the flag (which stripe it is in,
and how far down that stripe's run in its column it sits) and a shade (how dark it is against its stripe's brightest
pixel). A new flag repaints each pixel with the colour of its own stripes at that place, darkened by the same shade, so
it hangs and folds as the rainbow one does. The nails and cord, which are grey, are kept.

    python Tools/_WG/PrideFlags/pride_flags.py
"""
import json
import math
import os

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SOURCE = os.path.join(ROOT, "Resources", "Textures", "Structures", "Wallmounts", "flags.rsi", "lgbtq_flag.png")
OUT = os.path.join(ROOT, "Resources", "Textures", "_WG", "PrideFlags", "flags.rsi")

# The rainbow flag's six stripes, top to bottom, by their brightest pixel.
RAINBOW = [(0xBF, 0x26, 0x26), (0xC9, 0x68, 0x00), (0xC6, 0xC0, 0x00), (0x1C, 0x93, 0x2C), (0x35, 0x35, 0xB2),
           (0x8F, 0x23, 0xAF)]

# Black cloth reads as a hole in the wall at this size: it is drawn this dark instead.
BLACK = "#2a2a2a"

# state: stripes top to bottom, each a colour or (colour, weight).
FLAGS = {
    "gay": ["#078d70", "#26ceaa", "#98e8c1", "#ffffff", "#7bade2", "#5049cc", "#3d1a78"],
    "lesbian": ["#d52d00", "#ef7627", "#ff9a56", "#ffffff", "#d162a4", "#b55690", "#a30262"],
    "bisexual": [("#d60270", 2), ("#9b4f96", 1), ("#0038a8", 2)],
    "transgender": ["#5bcefa", "#f5a9b8", "#ffffff", "#f5a9b8", "#5bcefa"],
    "pansexual": ["#ff218c", "#ffd800", "#21b1ff"],
    "asexual": [BLACK, "#a3a3a3", "#ffffff", "#800080"],
    "aromantic": ["#3da542", "#a7d379", "#ffffff", "#a9a9a9", BLACK],
    "nonbinary": ["#fcf434", "#ffffff", "#9c59d1", BLACK],
    "genderfluid": ["#ff76a4", "#ffffff", "#c011d7", BLACK, "#2f3cbe"],
    "agender": [BLACK, "#bcc4c7", "#ffffff", "#b7f684", "#ffffff", "#bcc4c7", BLACK],
    "genderqueer": ["#b57edc", "#ffffff", "#4a8123"],
    "intersex": ["#ffd800"],
}

# The intersex flag's ring, in pixels from the cloth's centre.
RING = (2.6, 4.0)
RING_COLOUR = "#7902aa"


def rgb(colour):
    colour = colour.lstrip("#")
    return tuple(int(colour[i:i + 2], 16) for i in (0, 2, 4))


def stripe_of(pixel):
    """The rainbow stripe a cloth pixel belongs to: the one whose hue is nearest."""
    r, g, b = pixel[:3]
    top = max(r, g, b) or 1
    norm = (r / top, g / top, b / top)
    best, best_d = 0, float("inf")
    for i, (sr, sg, sb) in enumerate(RAINBOW):
        st = max(sr, sg, sb)
        d = sum((a - c / st) ** 2 for a, c in zip(norm, (sr, sg, sb)))
        if d < best_d:
            best, best_d = i, d
    return best


def is_cloth(pixel):
    """Coloured, not the grey of the nails and cord."""
    r, g, b, a = pixel
    return a > 0 and max(r, g, b) - min(r, g, b) > 24


def bands(stripes):
    """Each stripe's colour and where it ends, as a fraction of the flag's height."""
    weighted = [s if isinstance(s, tuple) else (s, 1) for s in stripes]
    total = sum(w for _, w in weighted)
    out, acc = [], 0
    for colour, weight in weighted:
        acc += weight
        out.append((rgb(colour), acc / total))
    return out


def paint(base, stripes, ring=None):
    w, h = base.size
    src = base.load()
    img = base.copy()
    dst = img.load()
    cloth = [(x, y) for x in range(w) for y in range(h) if is_cloth(src[x, y])]
    cx = sum(x for x, _ in cloth) / len(cloth)
    cy = sum(y for _, y in cloth) / len(cloth)
    flag = bands(stripes)
    for x in range(w):
        # Runs of one stripe down this column, so a pixel knows how far down its stripe it is.
        column = [(y, stripe_of(src[x, y])) for y in range(h) if is_cloth(src[x, y])]
        runs = {}
        for y, s in column:
            runs.setdefault(s, []).append(y)
        for y, s in column:
            run = runs[s]
            k = run.index(y)
            place = (s + (k + 0.5) / len(run)) / len(RAINBOW)
            colour = next(c for c, end in flag if place <= end + 1e-9)
            if ring and ring[0] <= math.dist((x + 0.5, y + 0.5), (cx + 0.5, cy + 0.5)) <= ring[1]:
                colour = rgb(RING_COLOUR)
            r, g, b, a = src[x, y]
            shade = max(r, g, b) / max(RAINBOW[s])
            dst[x, y] = tuple(min(255, round(c * shade)) for c in colour) + (a,)
    return img


def main():
    base = Image.open(SOURCE).convert("RGBA")
    os.makedirs(OUT, exist_ok=True)
    for state, stripes in FLAGS.items():
        paint(base, stripes, RING if state == "intersex" else None).save(os.path.join(OUT, f"{state}.png"))
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Repainted by Tools/_WG/PrideFlags/pride_flags.py from Structures/Wallmounts/flags.rsi lgbtq_flag "
                     "(derived from tgstation at https://github.com/tgstation/tgstation/commit/"
                     "d0d81185f09ca30d3b0856d476544240dba0de53, adapted by Saveliy360)",
        "size": {"x": base.size[0], "y": base.size[1]},
        "states": [{"name": state} for state in FLAGS],
    }
    with open(os.path.join(OUT, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")
    print(f"{len(FLAGS)} flags -> {OUT}")


if __name__ == "__main__":
    main()
