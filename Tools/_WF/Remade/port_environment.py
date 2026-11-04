"""
Builds Resources/Textures/_WF/Remade/Environment/decor.rsi: the ambient flesh, gore and glyph sprites the Remade module
scatters over corruption. Gore, organs and veins are DS13's own art recoloured; sacs, spurs, drips, cocoons, screens and haze
are drawn here in the corruption palette. Run from the repo root: python Tools/_WF/Remade/port_environment.py [--sheet out.png]
"""
from __future__ import annotations

import argparse
import json
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / "Tools/_WF/Genitals"))
import dmi  # noqa: E402

OUT = REPO / "Resources/Textures/_WF/Remade/Environment/decor.rsi"
OUT_TALL = REPO / "Resources/Textures/_WF/Remade/Environment/cocoon.rsi"
FONT = REPO / "Resources/Fonts/_DS13/Shage/Shage.ttf"

# The corruption's own colours, sampled from DS13's growth and floor sprites.
DARK = (40, 22, 30)
FLESH_D = (62, 30, 38)
FLESH = (92, 41, 47)
FLESH_L = (126, 56, 56)
WET = (170, 100, 92)
SINEW = (107, 85, 76)
BONE = (205, 194, 170)
BONE_D = (140, 128, 108)
NECRO_G = (120, 150, 90)

SOURCE_V1 = "https://github.com/DS-13-Dev-Team/DS13-1.0"
SOURCE_V2 = "https://github.com/DS-13-Dev-Team/DS13-2.0"


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def ramp(luma: float, stops):
    """Maps 0..1 onto a list of colour stops."""
    luma = max(0.0, min(1.0, luma))
    scaled = luma * (len(stops) - 1)
    i = min(int(scaled), len(stops) - 2)
    return lerp(stops[i], stops[i + 1], scaled - i)


def recolor(img: Image.Image, stops, alpha_scale=1.0) -> Image.Image:
    """Greyscale (or any) art onto a colour ramp by luminance, keeping its alpha."""
    img = img.convert("RGBA")
    out = Image.new("RGBA", img.size)
    px_in, px_out = img.load(), out.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px_in[x, y]
            if a == 0:
                continue
            luma = (0.3 * r + 0.59 * g + 0.11 * b) / 255.0
            px_out[x, y] = (*ramp(luma, stops), int(a * alpha_scale))
    return out


def hue_to_flesh(img: Image.Image) -> Image.Image:
    """Blue necromorph organs onto raw red-pink flesh, keeping their shading."""
    return recolor(img, [(70, 26, 34), FLESH_L, (190, 92, 92), (232, 168, 150)])


def dmi_state(path: Path, name: str, frame: int = 0) -> Image.Image:
    _, _, states = dmi.load_dmi(path)
    for st in states:
        if st["name"] == name:
            img = st["img"][0]
            if isinstance(img, list):
                img = img[min(frame, len(img) - 1)]
            return img.convert("RGBA")
    raise KeyError(name)


def blob(canvas, cx, cy, rx, ry, rnd, shade=1.0):
    """A shaded wet lump: dark rim, flesh body, a specular nick, veins."""
    d = ImageDraw.Draw(canvas)
    d.ellipse([cx - rx - 1, cy - ry - 1, cx + rx + 1, cy + ry + 1], fill=DARK + (255,))
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=FLESH_D + (255,))
    d.ellipse([cx - rx + 1, cy - ry + 1, cx + rx - 1, cy + ry - 1], fill=lerp(FLESH, FLESH_D, 1 - shade) + (255,))
    d.ellipse([cx - rx + 2, cy - ry + 1, cx + 1, cy + 1], fill=lerp(FLESH_L, FLESH, 1 - shade) + (255,))
    d.rectangle([cx - rx // 2 + 1, cy - ry // 2, cx - rx // 2 + 2, cy - ry // 2 + 1], fill=WET + (255,))
    for _ in range(max(3, rx)):
        a = rnd.uniform(0, math.tau)
        d.line([cx, cy, cx + math.cos(a) * (rx - 1), cy + math.sin(a) * (ry - 1)], fill=FLESH_D + (255,))


def draw_sac(frame: int, big: bool, seed: int) -> Image.Image:
    rnd = random.Random(seed)
    img = Image.new("RGBA", (32, 32))
    d = ImageDraw.Draw(img)
    pulse = [0, 1, 2, 1][frame]
    # Roots it hangs on.
    for _ in range(5):
        a = rnd.uniform(0, math.tau)
        L = rnd.randint(8, 14)
        d.line([16, 16, 16 + math.cos(a) * L, 16 + math.sin(a) * L], fill=FLESH_D + (255,), width=2)
        d.line([16, 16, 16 + math.cos(a) * L, 16 + math.sin(a) * L], fill=SINEW + (255,), width=1)
    if big:
        blob(img, 16, 16, 7 + pulse // 2, 6 + pulse // 2, rnd, 0.8 + 0.1 * pulse)
        blob(img, 11, 19, 3, 3, rnd)
        blob(img, 21, 11, 3, 2, rnd)
    else:
        blob(img, 16, 16, 5 + pulse // 2, 4 + pulse // 2, rnd, 0.8 + 0.1 * pulse)
        blob(img, 21, 20, 2, 2, rnd)
    return img


def draw_spur(seed: int) -> Image.Image:
    rnd = random.Random(seed)
    img = Image.new("RGBA", (32, 32))
    d = ImageDraw.Draw(img)
    # Dark meat around the base.
    d.ellipse([8, 15, 24, 27], fill=FLESH_D + (255,))
    d.ellipse([10, 17, 22, 25], fill=FLESH + (255,))
    for i in range(rnd.randint(3, 5)):
        bx = rnd.randint(10, 22)
        by = rnd.randint(19, 25)
        tx = bx + rnd.randint(-5, 5)
        ty = by - rnd.randint(8, 14)
        d.polygon([(bx - 2, by), (bx + 2, by), (tx, ty)], fill=BONE_D + (255,))
        d.polygon([(bx - 1, by), (bx + 1, by), (tx, ty)], fill=BONE + (255,))
        d.line([bx - 2, by, bx + 2, by], fill=FLESH_D + (255,))
    return img


def draw_organs(frame: int) -> Image.Image:
    img = Image.new("RGBA", (32, 32))
    d = ImageDraw.Draw(img)
    d.ellipse([3, 14, 29, 29], fill=DARK + (255,))
    d.ellipse([5, 16, 27, 27], fill=FLESH_D + (255,))
    return img


def draw_drip(frame: int) -> Image.Image:
    """Ooze sliding down a wall face: a smear and a bead that falls, four frames."""
    img = Image.new("RGBA", (32, 32))
    d = ImageDraw.Draw(img)
    d.rectangle([12, 2, 19, 5], fill=FLESH_D + (255,))
    d.rectangle([13, 3, 18, 5], fill=FLESH + (255,))
    d.rectangle([15, 5, 16, 8 + frame * 2], fill=FLESH + (255,))
    d.point([15, 6], fill=WET + (255,))
    bead = 10 + frame * 5
    d.ellipse([14, bead, 17, bead + 3], fill=FLESH_L + (255,))
    d.point([15, bead + 1], fill=WET + (255,))
    if frame == 3:
        d.ellipse([12, 28, 19, 30], fill=FLESH_D + (200,))
    return img


def draw_cocoon(seed: int) -> Image.Image:
    """Something that was a crewman, fused into the wall: a ribbed sac with a body shape pressed into it."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (32, 48))
    d = ImageDraw.Draw(img)
    d.ellipse([5, 2, 27, 46], fill=DARK + (255,))
    d.ellipse([6, 3, 26, 45], fill=FLESH_D + (255,))
    d.ellipse([8, 5, 24, 43], fill=lerp(FLESH_D, FLESH, 0.5) + (255,))
    # Head, shoulders, hands pressed from inside.
    d.ellipse([12, 8, 20, 17], fill=lerp(FLESH, SINEW, 0.5) + (255,))
    d.ellipse([13, 10, 15, 12], fill=DARK + (255,))
    d.ellipse([17, 10, 19, 12], fill=DARK + (255,))
    d.line([10, 21, 22, 21], fill=SINEW + (255,), width=3)
    d.line([10, 22, 7, 32], fill=SINEW + (255,), width=2)
    d.line([22, 22, 25, 32], fill=SINEW + (255,), width=2)
    d.rectangle([12, 22, 20, 34], fill=lerp(FLESH, SINEW, 0.3) + (255,))
    d.line([16, 34, 14, 42], fill=SINEW + (255,), width=2)
    d.line([17, 34, 19, 42], fill=SINEW + (255,), width=2)
    for y in range(7, 42, 5):
        d.arc([6, y, 26, y + 8], 20, 160, fill=FLESH_D + (255,))
    for _ in range(40):
        x, y = rnd.randint(7, 24), rnd.randint(4, 44)
        if img.getpixel((x, y))[3]:
            d.point([x, y], fill=FLESH_L + (255,) if rnd.random() < 0.4 else DARK + (255,))
    # Roots to the wall.
    for sx in (4, 27):
        d.line([sx, 6, sx + (1 if sx < 16 else -1), 40], fill=SINEW + (255,))
    return img


def draw_screen(frame: int, seed: int) -> Image.Image:
    """A dead monitor face writing the Monolith's glyphs, flickering. Shage's uppercase letters are the glyphs."""
    img = Image.new("RGBA", (32, 32))
    d = ImageDraw.Draw(img)
    rnd = random.Random(seed + frame * 31)
    font = ImageFont.truetype(str(FONT), 7)
    d.rectangle([5, 6, 26, 22], fill=(22, 6, 10, 235))
    glow = [(190, 40, 40), (140, 30, 30), (210, 70, 60), (120, 24, 28)][frame]
    text = Image.new("RGBA", (20, 15))
    td = ImageDraw.Draw(text)
    for row in range(3):
        line = "".join(rnd.choice("ABCDEFGHJKLMNPRSTUVWXYZ") for _ in range(3))
        td.text((1, row * 5 - 1), line, font=font, fill=glow + (255,))
    img.alpha_composite(text, (6, 7))
    if frame in (1, 3):
        y = rnd.randint(7, 20)
        d.rectangle([5, y, 26, y], fill=(0, 0, 0, 255))
    d.rectangle([5, 6, 26, 22], outline=FLESH_D + (255,))
    return img


def draw_haze(frame: int, seed: int) -> Image.Image:
    """A faint sour mist, drifting. Soft red-grey blobs at low alpha."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (32, 32))
    px = img.load()
    cx = [10.0, 22.0, 16.0]
    cy = [12.0, 20.0, 8.0]
    shift = frame * 2.0
    for y in range(32):
        for x in range(32):
            v = 0.0
            for i in range(3):
                dx = ((x - (cx[i] + shift * (1 if i % 2 else -1))) + 16) % 32 - 16
                dy = (y - cy[i] + 16) % 32 - 16
                v += math.exp(-(dx * dx + dy * dy) / (2 * (5.0 + i) ** 2))
            v = min(v, 1.0)
            if v > 0.12 and rnd.random() < 0.9:
                px[x, y] = (*lerp((70, 30, 34), (120, 70, 66), v), int(80 * v))
    return img


def write_rsi(out: Path, states: dict, width: int, height: int) -> None:
    out.mkdir(parents=True, exist_ok=True)
    for old in out.glob("*.png"):
        old.unlink()

    meta_states = []
    for name, (frames, delays) in states.items():
        if len(frames) == 1:
            frames[0].save(out / f"{name}.png")
            meta_states.append({"name": name})
        else:
            sheet = Image.new("RGBA", (width * len(frames), height))
            for i, frame in enumerate(frames):
                sheet.alpha_composite(frame, (i * width, 0))
            sheet.save(out / f"{name}.png")
            meta_states.append({"name": name, "delays": [delays or [0.5] * len(frames)]})

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": f"Gore, organs and veins from DS13-1.0 ({SOURCE_V1}, icons/effects/blood.dmi and spacevines.dmi) and DS13-2.0 ({SOURCE_V2}, necromorph_organ.dmi), recoloured; the rest drawn by Tools/_WF/Remade/port_environment.py",
        "size": {"x": width, "y": height},
        "states": meta_states,
    }
    (out / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"{out.name}: {len(meta_states)} states")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ds13-v1", default="C:/Users/jzo12/Documents/GitHub/DS13-1.0")
    parser.add_argument("--ds13", default="C:/Users/jzo12/Documents/GitHub/DS13-2.0")
    parser.add_argument("--sheet")
    args = parser.parse_args()

    blood = Path(args.ds13_v1) / "icons/effects/blood.dmi"
    vines = Path(args.ds13_v1) / "icons/effects/spacevines.dmi"
    organs = Path(args.ds13) / "deadspace/icons/obj/necromorph_organ.dmi"

    states: dict[str, tuple[list[Image.Image], list[float] | None]] = {}

    for i, big in enumerate((True, False, True)):
        states[f"sac{i + 1}"] = ([draw_sac(f, big, 11 * i + 5) for f in range(4)], [0.55, 0.35, 0.55, 0.9])
    for i in range(3):
        states[f"spur{i + 1}"] = ([draw_spur(40 + i)], None)

    # Organ clusters: DS13's organs on the flesh ramp, heaped on a bed of meat.
    for variant, names in enumerate((("heart", "lungs"), ("brain2", "liver", "eyes"), ("lungs", "tongue", "heart"))):
        frames = []
        for f in range(2):
            img = draw_organs(f)
            for k, name in enumerate(names):
                part = hue_to_flesh(dmi_state(organs, name))
                bbox = part.getbbox()
                if bbox:
                    part = part.crop(bbox)
                    x = 4 + k * 8 + (variant % 2) * 2
                    y = 7 + (k % 2) * 7 + f
                    img.alpha_composite(part, (min(x, 32 - part.width), min(y, 32 - part.height)))
            frames.append(img)
        states[f"organs{variant + 1}"] = (frames, [0.7, 0.7])

    gore_names = ["gib1_flesh", "gib2_flesh", "gibup1_flesh", "gibmid2_flesh", "mfloor1", "mfloor2", "mfloor3", "mfloor4", "mgibbl1"]
    gore_ramp = [(35, 4, 8), (82, 12, 18), (128, 24, 28), (170, 52, 48)]
    for i, name in enumerate(gore_names):
        states[f"gore{i + 1}"] = ([recolor(dmi_state(blood, name), gore_ramp)], None)
    states["remains"] = ([recolor(dmi_state(blood, "remains"), [BONE_D, BONE, (232, 224, 204)])], None)
    states["mucus"] = ([recolor(dmi_state(blood, "mucus"), [(40, 50, 26), (90, 112, 56), (140, 168, 90)])], None)

    # Veins over doors, vents and machines: DS13's vine web in corruption colours.
    for i in range(3):
        states[f"web{i + 1}"] = ([recolor(dmi_state(vines, f"Light{i + 1}"), [FLESH_D, FLESH, SINEW])], None)

    states["drip"] = ([draw_drip(f) for f in range(4)], [0.45, 0.45, 0.45, 0.45])
    states["cocoon1"] = ([draw_cocoon(7)], None)
    states["cocoon2"] = ([draw_cocoon(19)], None)
    states["screen1"] = ([draw_screen(f, 3) for f in range(4)], [0.7, 0.12, 0.5, 0.18])
    states["screen2"] = ([draw_screen(f, 9) for f in range(4)], [0.4, 0.15, 0.9, 0.12])
    states["haze1"] = ([draw_haze(f, 3) for f in range(4)], [2.5, 2.5, 2.5, 2.5])
    states["haze2"] = ([draw_haze(f, 8) for f in range(4)], [3.0, 3.0, 3.0, 3.0])

    tall = {name: value for name, value in states.items() if name.startswith("cocoon")}
    flat = {name: value for name, value in states.items() if name not in tall}
    write_rsi(OUT, flat, 32, 32)
    write_rsi(OUT_TALL, tall, 32, 48)

    if args.sheet:
        cols = 8
        cell = 32 * 3 + 8
        rows = (len(states) + cols - 1) // cols
        sheet = Image.new("RGBA", (cols * cell, rows * (48 * 3 + 20)), (60, 62, 70, 255))
        d = ImageDraw.Draw(sheet)
        for i, (name, (frames, _)) in enumerate(states.items()):
            x, y = (i % cols) * cell, (i // cols) * (48 * 3 + 20)
            big = frames[0].resize((frames[0].width * 3, frames[0].height * 3), Image.NEAREST)
            sheet.alpha_composite(big, (x + 4, y + 4))
            d.text((x + 4, y + 48 * 3 + 6), name, fill=(255, 255, 255, 255))
        sheet.convert("RGB").save(args.sheet)


if __name__ == "__main__":
    main()
