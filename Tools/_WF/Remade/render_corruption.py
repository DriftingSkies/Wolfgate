#!/usr/bin/env python3
"""Renders a patch of corruption, the way the game smooths it, to check the art joins up.

    python Tools/_WF/Remade/render_corruption.py out.png [--size 6] [--seed 3] [--walls]

Each tile picks `corruption-<mask>` from its neighbours (N 1, S 2, E 4, W 8, and a corner bit 16/32/64/128 only when both
sides next to it are set), exactly as WFCorruptionSystem works it out. Walls count as neighbours, like corruption does.
"""
from __future__ import annotations

import argparse
import random
from pathlib import Path

from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parents[3]
TEX = REPO / "Resources/Textures/_WF/Remade/Structures"
TILE = 32


def mask_for(cells: set, walls: set, x: int, y: int) -> int:
    def on(dx: int, dy: int) -> bool:
        return (x + dx, y + dy) in cells or (x + dx, y + dy) in walls

    n, s, e, w = on(0, -1), on(0, 1), on(1, 0), on(-1, 0)  # y grows down on the image
    mask = (1 if n else 0) | (2 if s else 0) | (4 if e else 0) | (8 if w else 0)
    if n and e and on(1, -1):
        mask |= 16
    if s and e and on(1, 1):
        mask |= 32
    if s and w and on(-1, 1):
        mask |= 64
    if n and w and on(-1, -1):
        mask |= 128
    return mask


def wall_mask(cells: set, x: int, y: int) -> int:
    return ((1 if (x, y - 1) in cells else 0) | (2 if (x, y + 1) in cells else 0)
            | (4 if (x + 1, y) in cells else 0) | (8 if (x - 1, y) in cells else 0))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("out")
    parser.add_argument("--size", type=int, default=6)
    parser.add_argument("--seed", type=int, default=3)
    parser.add_argument("--walls", action="store_true", help="ring the patch with walls and draw their growth")
    parser.add_argument("--scale", type=int, default=4)
    args = parser.parse_args()

    rng = random.Random(args.seed)
    n = args.size
    pad = 3
    cells = {(x, y) for x in range(pad, pad + n) for y in range(pad, pad + n)}
    # Ragged edge: drop a few edge tiles and add a few outside, keeping it one blob.
    for x, y in list(cells):
        if (x in (pad, pad + n - 1) or y in (pad, pad + n - 1)) and rng.random() < 0.3:
            cells.discard((x, y))
    for _ in range(n):
        x, y = rng.choice(sorted(cells))
        dx, dy = rng.choice([(1, 0), (-1, 0), (0, 1), (0, -1)])
        cells.add((x + dx, y + dy))

    size = n + pad * 2
    walls = set()
    if args.walls:
        walls = {(x, y) for x in range(size) for y in range(size) if x in (1, size - 2) or y in (1, size - 2)}
        walls |= {(x, 5) for x in range(1, 4)}
        # A room: the carpet fills its west end, runs out ragged part way along, and presses against three walls.
        cells = {(x, y) for x in range(2, size - 2) for y in range(2, size - 2)
                 if (x, y) not in walls and x < size // 2 + rng.choice([0, 0, 1, 2])}
        cells = {c for c in cells if not (c[0] > 2 and rng.random() < 0.12 and c[0] >= size // 2)}

    img = Image.new("RGBA", (size * TILE, size * TILE), (0, 0, 0, 255))
    draw = ImageDraw.Draw(img)
    for x in range(size):
        for y in range(size):
            shade = 52 if (x + y) % 2 == 0 else 58
            draw.rectangle([x * TILE, y * TILE, x * TILE + TILE - 1, y * TILE + TILE - 1], fill=(shade, shade + 4, shade + 8, 255))
    for x, y in walls:
        draw.rectangle([x * TILE, y * TILE, x * TILE + TILE - 1, y * TILE + TILE - 1], fill=(120, 126, 134, 255))
        draw.rectangle([x * TILE, y * TILE + 22, x * TILE + TILE - 1, y * TILE + TILE - 1], fill=(88, 94, 102, 255))

    for x, y in sorted(cells):
        mask = mask_for(cells, walls, x, y)
        state = f"full-{1 + (x * 7 + y * 13) % 3}" if mask == 255 else f"corruption-{mask}"
        tile = Image.open(TEX / "corruption.rsi" / f"{state}.png").convert("RGBA")
        img.alpha_composite(tile, (x * TILE, y * TILE))

    wall_rsi = TEX / "wall_growth.rsi"
    if walls and wall_rsi.exists():
        for x, y in sorted(walls):
            m = wall_mask(cells, x, y)
            if m:
                img.alpha_composite(Image.open(wall_rsi / f"wall-{m}.png").convert("RGBA"), (x * TILE, y * TILE))

    img = img.resize((img.width * args.scale, img.height * args.scale), Image.NEAREST)
    img.save(args.out)
    print(f"{args.out}: {len(cells)} tiles")


if __name__ == "__main__":
    main()
