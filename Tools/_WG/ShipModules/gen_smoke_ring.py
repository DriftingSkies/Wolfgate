"""Generates smoke_ring.rsi for the maneuvering jets: a ring of smoke puffs standing across a thruster's exhaust,
seen from above, growing and breaking up over four frames.

Usage: python Tools/_WG/ShipModules/gen_smoke_ring.py [out.rsi]
"""
import json, math, os, sys
import numpy as np
from PIL import Image

SIZE = 32
FRAMES = 4
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "Resources", "Textures", "_WG", "ShipModules", "smoke_ring.rsi")

def periodic_noise(n, count, seed):
    """Smooth noise around the circle: n angles -> values in [-1, 1]."""
    r = np.random.default_rng(seed).uniform(-1, 1, count)
    t = np.linspace(0, count, n, endpoint=False)
    i = np.floor(t).astype(int)
    f = t - i
    f = f * f * (3 - 2 * f)
    return r[i % count] * (1 - f) + r[(i + 1) % count] * f


def lattice_noise(cell, seed):
    """Smooth 2D value noise over the canvas, cell px per lattice step."""
    n = SIZE // cell + 2
    g = np.random.default_rng(seed).uniform(0, 1, (n, n))
    y, x = np.mgrid[0:SIZE, 0:SIZE] / cell
    xi, yi = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = x - xi, y - yi
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a = g[yi, xi] * (1 - fx) + g[yi, xi + 1] * fx
    b = g[yi + 1, xi] * (1 - fx) + g[yi + 1, xi + 1] * fx
    return a * (1 - fy) + b * fy


y, x = np.mgrid[0:SIZE, 0:SIZE].astype(float)
cx = cy = (SIZE - 1) / 2
dx, dy = x - cx, y - cy
dist = np.hypot(dx, dy)
theta = np.arctan2(dy, dx)
angle_idx = ((theta + math.pi) / (2 * math.pi) * 720).astype(int) % 720

grain = lattice_noise(2, 11)        # faint texture inside each puff
# Authored with the exhaust pointing down the canvas: the ring stands across the exhaust, seen from above and a
# little behind, so it is a flat oval; its downstream arc is in front, its upstream arc behind. Light comes from
# the viewer, so the frames read the same at any rotation.
light = np.array([0.0, 0.35, 0.94])
light /= np.linalg.norm(light)

PUFFS = 22
prng = np.random.default_rng(5)
puff_angle = np.arange(PUFFS) * 2 * math.pi / PUFFS + prng.uniform(-0.12, 0.12, PUFFS)
puff_offset = prng.uniform(-0.35, 0.35, PUFFS)
puff_size = prng.uniform(0.85, 1.15, PUFFS)
puff_fade = prng.uniform(0.35, 1.25, PUFFS)
puff_drift = prng.uniform(-0.3, 0.6, PUFFS)
# Far arc first, so the near arc paints over it.
order = np.argsort(np.sin(puff_angle))

frames = []
for k in range(FRAMES):
    p = k / (FRAMES - 1)
    across = 9.2 + 1.9 * p
    along = 4.1 + 1.1 * p
    density = np.zeros((SIZE, SIZE))
    value = np.zeros((SIZE, SIZE))
    for i in order:
        near = math.sin(puff_angle[i])          # 1 at the front of the ring, -1 at the back
        grow = 1 + 0.06 * puff_offset[i] + 0.08 * puff_drift[i] * p
        px = cx + across * grow * math.cos(puff_angle[i])
        py = cy + along * grow * near + puff_offset[i] * 0.8
        r = (3.1 + 0.7 * p) * puff_size[i] * (0.82 + 0.18 * (near + 1) / 2)
        ux, uy = (x - px) / r, (y - py) / r
        q = ux * ux + uy * uy
        d = np.clip(1 - q, 0, 1) ** 0.65 * max(0.0, 1 - p * puff_fade[i]) * (0.72 + 0.28 * (near + 1) / 2)
        nz = np.sqrt(np.clip(1 - q, 0, 1))
        lit = np.clip(ux * light[0] + uy * light[1] + nz * light[2], 0, 1)
        v = 132 + 78 * lit + 22 * (near + 1) / 2
        top = (d > 0.3) | (d > density)
        value = np.where(top & (d > 0), v, value)
        density = np.maximum(density, d)
    value = value + 5 * (grain - 0.5)
    alpha = density * 230 * (1 - 0.45 * p)
    alpha = np.where(alpha < 12, 0, alpha)
    # Clean pass: drop pixels with no opaque 4-neighbour.
    solid = alpha > 0
    nb = np.zeros_like(solid)
    nb[1:] |= solid[:-1]; nb[:-1] |= solid[1:]; nb[:, 1:] |= solid[:, :-1]; nb[:, :-1] |= solid[:, 1:]
    alpha = np.where(solid & ~nb, 0, alpha)
    rgba = np.zeros((SIZE, SIZE, 4), np.uint8)
    rgba[..., 0] = np.clip(value, 0, 255)
    rgba[..., 1] = np.clip(value + 2, 0, 255)
    rgba[..., 2] = np.clip(value + 6, 0, 255)
    rgba[..., 3] = np.clip(alpha, 0, 255)
    rgba[rgba[..., 3] == 0] = 0
    frames.append(Image.fromarray(rgba, "RGBA"))

cols = math.ceil(math.sqrt(FRAMES))
rows = math.ceil(FRAMES / cols)
sheet = Image.new("RGBA", (cols * SIZE, rows * SIZE))
for i, f in enumerate(frames):
    sheet.paste(f, ((i % cols) * SIZE, (i // cols) * SIZE))

os.makedirs(OUT, exist_ok=True)
sheet.save(os.path.join(OUT, "ring.png"))
meta = {
    "version": 1,
    "license": "CC-BY-SA-3.0",
    "copyright": "Created for Wolfgate (ShipModules), procedurally generated; shading follows chemsmoke.rsi (CEV-Eris).",
    "size": {"x": SIZE, "y": SIZE},
    "states": [{"name": "ring", "delays": [[0.15] * FRAMES]}],
}
with open(os.path.join(OUT, "meta.json"), "w", newline="\n") as fh:
    json.dump(meta, fh, indent=4)
    fh.write("\n")
print("ok", sheet.size)
