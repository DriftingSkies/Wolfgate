"""Generates the rolled-sleeve and rolled-down masks from the human body silhouette.

The WFRolledSleeves shaders read a mask over the worn jumpsuit, per output pixel:
- alpha 0 cuts the pixel;
- red shades it: 128 leaves it, higher lightens, lower darkens;
- green and blue move where the suit is sampled from, in pixels: 128 + dx, 128 + dy (image space, y down).

States, each 4-direction 32x32 in the S, N, E, W frame order of an RSI:
- `sleeves`: cuts the sleeve below the elbow the way Meridian Rift draws it, with a one-pixel rolled cuff.
- `down`: cuts everything above the belt and draws the tied sleeves round the waist, sampled from the chest.

Usage:
    python Tools/_WF/RolledSleeves/generate_sleeve_mask.py
    python Tools/_WF/RolledSleeves/generate_sleeve_mask.py --preview out.png [rsi substrings...]
"""

import argparse
import json
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
TEXTURES = ROOT / "Resources" / "Textures"
BODY = TEXTURES / "Mobs" / "Species" / "Human" / "parts.rsi"
OUTPUT = TEXTURES / "_WF" / "RolledSleeves" / "sleeve_mask.rsi"

FRAME = 32
DIRS = ("S", "N", "E", "W")
NEUTRAL = (128, 128, 128, 255)
CUT = (128, 128, 128, 0)

# The belt row: rolling down cuts every row above it.
WAIST_ROW = 20
# How far up the tied sleeves sample the suit, to pick up the shirt rather than the belt or trousers.
CHEST_OFFSET = -6
BAND_SHADE = 104
KNOT_SHADE = 92


def opaque(name):
    img = Image.open(BODY / f"{name}.png").convert("RGBA")
    sets = []
    for d in range(4):
        ox, oy = (d % 2) * FRAME, (d // 2) * FRAME
        sets.append({(x, y) for x in range(FRAME) for y in range(FRAME) if img.getpixel((ox + x, oy + y))[3] > 0})
    return sets


def body_parts():
    """Per direction: each arm on its own, both hands, and the torso."""
    arms = list(zip(opaque("l_arm"), opaque("r_arm")))
    hands = [a | b for a, b in zip(opaque("l_hand"), opaque("r_hand"))]
    torso = [a | b for a, b in zip(opaque("torso_m"), opaque("torso_f"))]
    return arms, hands, torso


def sleeves_frame(d, arms, hands, torso):
    """Pixel -> mask colour for one direction of the rolled-sleeve state, after Meridian Rift's drawn cut.

    Facing S or N each sleeve loses its bottom three rows, except at the elbow row: the pixel against the torso stays
    and the one beside it becomes the rolled cuff, copied from the shade up and outward of it. Side-on only the bottom
    two rows go.
    """
    pixels = {}
    centre = sum(x for x, _ in torso[d]) / len(torso[d])
    for arm in arms[d]:
        if not arm:
            continue
        bottom = max(y for _, y in arm)
        rows = 3 if DIRS[d] in ("S", "N") else 2
        for x, y in arm:
            if y > bottom - rows:
                pixels[(x, y)] = CUT
        if DIRS[d] not in ("S", "N"):
            continue
        elbow = bottom - rows + 1
        xs = sorted((x for x, y in arm if y == elbow), key=lambda x: abs(x - centre))
        outward = 1 if xs[0] > centre else -1
        pixels.pop((xs[0], elbow))
        pixels[(xs[1], elbow)] = (128, 128 + outward, 128 - 1, 255)
    return pixels


def down_frame(d, arms, hands, torso):
    """Pixel -> mask colour for one direction of the rolled-down state."""
    pixels = {(x, y): CUT for x in range(FRAME) for y in range(WAIST_ROW)}
    chest = (128, 128 + CHEST_OFFSET)

    # The tied sleeves: two rows at the belt, a pixel wider than the waist each side where no hand hangs there.
    limbs = arms[d][0] | arms[d][1] | hands[d]
    for y in (WAIST_ROW, WAIST_ROW + 1):
        xs = [x for x in range(FRAME) if (x, y) in torso[d] and (x, y) not in limbs]
        if not xs:
            continue
        for x in range(min(xs) - 1, max(xs) + 2):
            if (x, y) not in limbs:
                pixels[(x, y)] = (BAND_SHADE, chest[0], chest[1], 255)

    # The knot: hanging at the front, so at the centre facing S or N and at the leading edge side-on.
    xs = sorted(x for x in range(FRAME) if (x, WAIST_ROW) in torso[d])
    if DIRS[d] in ("S", "N"):
        mid = (xs[0] + xs[-1]) // 2
        knot = [(mid - 1, WAIST_ROW + 2), (mid, WAIST_ROW + 2), (mid + 1, WAIST_ROW + 2), (mid, WAIST_ROW + 3)]
    else:
        edge = xs[-1] + 1 if DIRS[d] == "E" else xs[0] - 1
        knot = [(edge, WAIST_ROW + 2), (edge, WAIST_ROW + 3)]
    for p in knot:
        pixels[p] = (KNOT_SHADE, chest[0], chest[1] - 2, 255)
    return pixels


def build_masks():
    parts = body_parts()
    masks = {}
    for name, frame in (("sleeves", sleeves_frame), ("down", down_frame)):
        mask = Image.new("RGBA", (FRAME * 2, FRAME * 2), NEUTRAL)
        for d in range(4):
            ox, oy = (d % 2) * FRAME, (d // 2) * FRAME
            for (x, y), colour in frame(d, *parts).items():
                if 0 <= x < FRAME and 0 <= y < FRAME:
                    mask.putpixel((ox + x, oy + y), colour)
        masks[name] = mask
    return masks


def write(masks):
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for old in OUTPUT.glob("*.png"):
        old.unlink()
    for name, mask in masks.items():
        mask.save(OUTPUT / f"{name}.png", optimize=True)
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Generated by Tools/_WF/RolledSleeves/generate_sleeve_mask.py from the human body sprites "
                     "(Mobs/Species/Human/parts.rsi).",
        "size": {"x": FRAME, "y": FRAME},
        "load": {"srgb": False},
        "states": [{"name": name, "directions": 4} for name in masks],
    }
    (OUTPUT / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Wrote {OUTPUT.relative_to(ROOT)}")


def apply(suit, mask):
    """What the shader does, for the preview."""
    out = Image.new("RGBA", suit.size)
    for y in range(out.height):
        for x in range(out.width):
            mr, mg, mb, ma = mask.getpixel((x, y))
            if not ma:
                continue
            fx, fy = x - x % FRAME, y - y % FRAME
            sx, sy = x + mg - 128, y + mb - 128
            if not (fx <= sx < fx + FRAME and fy <= sy < fy + FRAME):
                continue
            r, g, b, a = suit.getpixel((sx, sy))
            shade = mr / 255 * 2 - 1
            if shade > 0:
                r, g, b = (int(c + (255 - c) * shade * 0.25) for c in (r, g, b))
            else:
                r, g, b = (int(c * (1 + shade * 0.5)) for c in (r, g, b))
            out.putpixel((x, y), (r, g, b, a))
    return out


def preview(out, filters, masks):
    """Draws each suit normal, with rolled sleeves and rolled down over a plain body, scaled up."""
    body = Image.new("RGBA", (FRAME * 2, FRAME * 2))
    for part in ("torso_m", "l_arm", "r_arm", "l_hand", "r_hand", "l_leg", "r_leg", "head_m"):
        layer = Image.open(BODY / f"{part}.png").convert("RGBA")
        body.alpha_composite(Image.composite(Image.new("RGBA", layer.size, (224, 172, 132, 255)),
                                             Image.new("RGBA", layer.size), layer.split()[3]))
    suits = []
    for png in sorted(TEXTURES.rglob("*.rsi/equipped-INNERCLOTHING.png")):
        if filters and not any(f in png.as_posix() for f in filters):
            continue
        img = Image.open(png).convert("RGBA")
        if img.size == (FRAME * 2, FRAME * 2):
            suits.append(img)
        if len(suits) >= 8:
            break
    sheet = Image.new("RGBA", (FRAME * 12, FRAME * len(suits)), (60, 60, 70, 255))
    for r, suit in enumerate(suits):
        for c, img in enumerate((suit, apply(suit, masks["sleeves"]), apply(suit, masks["down"]))):
            for d in range(4):
                ox, oy = (d % 2) * FRAME, (d // 2) * FRAME
                tile = body.crop((ox, oy, ox + FRAME, oy + FRAME))
                tile.alpha_composite(img.crop((ox, oy, ox + FRAME, oy + FRAME)))
                sheet.alpha_composite(tile, ((c * 4 + d) * FRAME, r * FRAME))
    sheet.resize((sheet.width * 6, sheet.height * 6), Image.NEAREST).save(out)
    print(f"Preview of {len(suits)} suits written to {out}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--preview", metavar="PNG")
    parser.add_argument("filters", nargs="*")
    args = parser.parse_args()
    masks = build_masks()
    if args.preview:
        preview(args.preview, args.filters, masks)
    else:
        write(masks)


if __name__ == "__main__":
    sys.exit(main())
