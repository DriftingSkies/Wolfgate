"""Draws the Drift alert: a small sanity-meter dial, one state per severity, an animated garbled one, and red ones for a Monolith's Drift."""
import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parents[3] / "Resources/Textures/_WF/Lattice/drift_alert.rsi"
SIZE = 32
SCALE = 8  # drawn large, then shrunk, for clean edges
LEVELS = 21  # one per Drift level (Drift / 5)
TREMBLE = 0.012

RIM = (201, 164, 92, 255)
FACE = (24, 14, 22, 235)
BANDS = [((106, 168, 138, 255), 0.0, 0.35), ((214, 162, 58, 255), 0.35, 0.7), ((176, 64, 224, 255), 0.7, 1.0)]
NEEDLE = (240, 224, 192, 255)
STONE_RIM = (196, 52, 52, 255)
STONE_BANDS = [((150, 60, 60, 255), 0.0, 0.35), ((196, 52, 52, 255), 0.35, 0.7), ((120, 10, 20, 255), 0.7, 1.0)]


def point(c, r, reading):
    angle = math.pi + math.pi * reading
    return c + math.cos(angle) * r, c * 1.18 + math.sin(angle) * r


def dial(reading, garbled=False, rng=None, stone=False):
    rim = STONE_RIM if stone else RIM
    bands = STONE_BANDS if stone else BANDS
    big = SIZE * SCALE
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = big / 2
    cy = c * 1.18
    r = big * 0.44
    d.pieslice([c - r, cy - r, c + r, cy + r], 180, 360, fill=FACE)
    d.rectangle([c - r, cy, c + r, cy + big * 0.08], fill=FACE)
    d.arc([c - r, cy - r, c + r, cy + r], 180, 360, fill=rim, width=SCALE * 2)
    inner = r - SCALE * 3
    for color, a, b in bands:
        if garbled:
            color = tuple(min(255, int(v * rng.uniform(0.4, 1.2))) for v in color[:3]) + (255,)
        d.arc([c - inner, cy - inner, c + inner, cy + inner], 180 + 180 * a, 180 + 180 * b, fill=color, width=SCALE * 3)
    for tick in range(11):
        x0, y0 = point(c, inner - SCALE * 2, tick / 10)
        x1, y1 = point(c, inner - SCALE * (5 if tick % 5 == 0 else 3.5), tick / 10)
        d.line([x0, y0, x1, y1], fill=rim, width=SCALE)
    if garbled:
        reading = rng.random()
        for _ in range(40):
            x, y = rng.uniform(c - r, c + r), rng.uniform(cy - r, cy)
            d.rectangle([x, y, x + SCALE, y + SCALE], fill=(176, 64, 224, rng.randint(80, 200)))
    x, y = point(c, inner - SCALE * 4, reading)
    d.line([c, cy, x, y], fill=NEEDLE, width=SCALE * 2)
    d.ellipse([c - SCALE * 2.5, cy - SCALE * 2.5, c + SCALE * 2.5, cy + SCALE * 2.5], fill=rim)
    return img.resize((SIZE, SIZE), Image.LANCZOS)


def strip(frames):
    sheet = Image.new("RGBA", (SIZE * len(frames), SIZE))
    for i, frame in enumerate(frames):
        sheet.paste(frame, (SIZE * i, 0))
    return sheet


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    states = []
    for prefix, stone in (("drift", False), ("stone", True)):
        for level in range(LEVELS):
            reading = level / (LEVELS - 1)
            # A faint tremble, so the needle never sits dead still.
            frames = [dial(min(1, max(0, reading + offset)), stone=stone) for offset in (0, TREMBLE, 0, -TREMBLE)]
            strip(frames).save(OUT / f"{prefix}{level}.png")
            states.append({"name": f"{prefix}{level}", "delays": [[0.25, 0.15, 0.25, 0.15]]})

    rng = random.Random(7)
    strip([dial(0, True, rng) for _ in range(6)]).save(OUT / "garbled.png")
    states.append({"name": "garbled", "delays": [[0.12, 0.08, 0.15, 0.06, 0.1, 0.09]]})

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Made for Wolfgate by Tools/_WF/Lattice/gen_drift_alert.py",
        "size": {"x": SIZE, "y": SIZE},
        "states": states,
    }
    (OUT / "meta.json").write_text(json.dumps(meta, indent=4) + "\n")


if __name__ == "__main__":
    main()
