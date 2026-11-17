#!/usr/bin/env python3
"""Generates the ship_speaker RSI: procedural 32x32 pixel art, no external deps.

Draws a light-grey wallmounted speaker cabinet (the `base` layer), a status LED
layer (`led_off`/`led_idle`/`led_active`) and a damage overlay layer
(`cracked`/`broken`), then emits South/North/East/West frames per state as a
direction-major horizontal strip (engine convention: all of South's frames,
then all of North's, then East's, then West's) plus meta.json.

Usage: python Tools/_WG/ShipPa/gen_speaker_rsi.py
Writes into Resources/Textures/_WG/ShipPa/Structures/Wallmounts/ship_speaker.rsi/
"""

import json
import os
import struct
import zlib

FRAME = 32
OUT_DIR = os.path.normpath(os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "..",
    "Resources", "Textures", "_WG", "ShipPa", "Structures", "Wallmounts", "ship_speaker.rsi",
))

# South-facing cabinet bounding box (device hugs the bottom edge of the tile).
SX0, SY0, SX1, SY1 = 6, 18, 25, 30  # inclusive

# Colors (RGBA).
TRANSPARENT = (0, 0, 0, 0)
CABINET_FILL = (176, 180, 186, 255)
CABINET_BORDER = (90, 94, 99, 255)
CABINET_HILITE = (214, 217, 221, 255)
CABINET_SHADOW = (66, 69, 73, 255)
GRILLE = (32, 34, 37, 255)
BEZEL = (150, 154, 160, 255)
BEZEL_BORDER = (110, 113, 118, 255)
LED_OFF = (58, 48, 48, 255)
LED_IDLE = (63, 224, 90, 255)
LED_ACTIVE = (255, 176, 32, 255)
LED_ACTIVE_DIM = (140, 96, 24, 255)
CRACK = (24, 22, 24, 255)
SOOT = (72, 68, 66, 210)
BROKEN_GAP = (12, 12, 14, 255)

# Bezel plate + LED dot position, South-relative. The LED sits inside the bezel.
BEZEL_X0, BEZEL_Y0, BEZEL_X1, BEZEL_Y1 = SX0 + 2, SY0 + 1, SX0 + 5, SY0 + 4
LED_X0, LED_Y0 = SX0 + 3, SY0 + 2  # 2x2 dot

# Fixed soot fleck offsets for the broken state (South-relative, deterministic).
SOOT_PX = [
    (SX0 + 3, SY1 - 2), (SX0 + 5, SY1 - 1), (SX0 + 8, SY1 - 3),
    (SX1 - 6, SY1 - 2), (SX1 - 4, SY1 - 4), (SX1 - 2, SY1 - 1),
]


def new_canvas():
    return [[TRANSPARENT for _ in range(FRAME)] for _ in range(FRAME)]


def set_px(canvas, x, y, color):
    if 0 <= x < FRAME and 0 <= y < FRAME:
        canvas[y][x] = color


def fill_rect(canvas, x0, y0, x1, y1, color):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            set_px(canvas, x, y, color)


def rect_outline(canvas, x0, y0, x1, y1, color):
    for x in range(x0, x1 + 1):
        set_px(canvas, x, y0, color)
        set_px(canvas, x, y1, color)
    for y in range(y0, y1 + 1):
        set_px(canvas, x0, y, color)
        set_px(canvas, x1, y, color)


def hline(canvas, x0, x1, y, color):
    for x in range(x0, x1 + 1):
        set_px(canvas, x, y, color)


def line(canvas, x0, y0, x1, y1, color):
    """Deterministic integer Bresenham line."""
    dx = abs(x1 - x0)
    dy = -abs(y1 - y0)
    sx = 1 if x0 < x1 else -1
    sy = 1 if y0 < y1 else -1
    err = dx + dy
    x, y = x0, y0
    while True:
        set_px(canvas, x, y, color)
        if x == x1 and y == y1:
            break
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x += sx
        if e2 <= dx:
            err += dx
            y += sy


def draw_cabinet(canvas):
    fill_rect(canvas, SX0, SY0, SX1, SY1, CABINET_FILL)
    rect_outline(canvas, SX0, SY0, SX1, SY1, CABINET_BORDER)

    # Bevel: light on the top/left inner edge, dark on the bottom/right inner edge.
    hline(canvas, SX0 + 1, SX1 - 1, SY0 + 1, CABINET_HILITE)
    for y in range(SY0 + 1, SY1):
        set_px(canvas, SX0 + 1, y, CABINET_HILITE)
    hline(canvas, SX0 + 1, SX1 - 1, SY1 - 1, CABINET_SHADOW)
    for y in range(SY0 + 1, SY1):
        set_px(canvas, SX1 - 1, y, CABINET_SHADOW)

    # Grille slits.
    for row in (SY0 + 3, SY0 + 5, SY0 + 7, SY0 + 9):
        hline(canvas, SX0 + 2, SX1 - 2, row, GRILLE)

    # Bezel plate (drawn last so it sits cleanly over the grille's top-left corner).
    fill_rect(canvas, BEZEL_X0, BEZEL_Y0, BEZEL_X1, BEZEL_Y1, BEZEL)
    rect_outline(canvas, BEZEL_X0, BEZEL_Y0, BEZEL_X1, BEZEL_Y1, BEZEL_BORDER)


def draw_led(canvas, color):
    fill_rect(canvas, LED_X0, LED_Y0, LED_X0 + 1, LED_Y0 + 1, color)


def draw_cracked(canvas):
    line(canvas, SX0 + 4, SY0 + 2, SX0 + 10, SY0 + 8, CRACK)
    line(canvas, SX0 + 11, SY0 + 3, SX0 + 16, SY0 + 9, CRACK)


def draw_broken(canvas):
    # Wider crack: two adjacent diagonals plus a branch.
    line(canvas, SX0 + 3, SY0 + 1, SX0 + 12, SY0 + 10, CRACK)
    line(canvas, SX0 + 4, SY0 + 1, SX0 + 13, SY0 + 10, CRACK)
    line(canvas, SX0 + 13, SY0 + 2, SX1 - 2, SY0 + 9, CRACK)
    # Torn grille: a punched-out gap over one slit.
    fill_rect(canvas, SX0 + 7, SY0 + 6, SX1 - 5, SY0 + 7, BROKEN_GAP)
    for (x, y) in SOOT_PX:
        set_px(canvas, x, y, SOOT)


def flip_v(canvas):
    """South (bottom-hugging) -> North (top-hugging)."""
    return [canvas[FRAME - 1 - y][:] for y in range(FRAME)]


def rotate_cw(canvas):
    """South (bottom-hugging) -> West (left-hugging)."""
    out = new_canvas()
    for y in range(FRAME):
        for x in range(FRAME):
            out[y][x] = canvas[FRAME - 1 - x][y]
    return out


def rotate_ccw(canvas):
    """South (bottom-hugging) -> East (right-hugging)."""
    out = new_canvas()
    for y in range(FRAME):
        for x in range(FRAME):
            out[y][x] = canvas[x][FRAME - 1 - y]
    return out


def directions(canvas):
    """South, North, East, West frames derived from one South-oriented canvas."""
    return [canvas, flip_v(canvas), rotate_ccw(canvas), rotate_cw(canvas)]


def chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff))


def write_png(path, height, columns):
    """columns: 32x32 canvases laid out left-to-right into one image."""
    width = FRAME * len(columns)
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # filter: None
        for canvas in columns:
            for x in range(FRAME):
                raw += bytes(canvas[y][x])
    compressed = zlib.compress(bytes(raw), 9)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    png = bytearray(b"\x89PNG\r\n\x1a\n")
    png += chunk(b"IHDR", ihdr)
    png += chunk(b"IDAT", compressed)
    png += chunk(b"IEND", b"")

    with open(path, "wb") as f:
        f.write(png)


def build_state(name, frame_sets, delays=None):
    """frame_sets: South-oriented canvases, one per animation frame (in order).
    Writes <name>.png as direction-major columns: all South frames, then all
    North, East, West. Returns the meta.json state entry.
    """
    per_dir = [[], [], [], []]  # South, North, East, West
    for south_canvas in frame_sets:
        for i, frame in enumerate(directions(south_canvas)):
            per_dir[i].append(frame)

    columns = per_dir[0] + per_dir[1] + per_dir[2] + per_dir[3]
    write_png(os.path.join(OUT_DIR, name + ".png"), FRAME, columns)

    state = {"name": name, "directions": 4}
    if delays:
        state["delays"] = [delays[:] for _ in range(4)]
    return state


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    base = new_canvas()
    draw_cabinet(base)

    led_off = new_canvas()
    draw_led(led_off, LED_OFF)

    led_idle = new_canvas()
    draw_led(led_idle, LED_IDLE)

    led_active_a = new_canvas()
    draw_led(led_active_a, LED_ACTIVE)
    led_active_b = new_canvas()
    draw_led(led_active_b, LED_ACTIVE_DIM)

    cracked = new_canvas()
    draw_cracked(cracked)

    broken = new_canvas()
    draw_broken(broken)

    states = [
        build_state("base", [base]),
        build_state("led_off", [led_off]),
        build_state("led_idle", [led_idle]),
        build_state("led_active", [led_active_a, led_active_b], delays=[0.4, 0.4]),
        build_state("cracked", [cracked]),
        build_state("broken", [broken]),
    ]

    meta = {
        "version": 1,
        "license": "CC0-1.0",
        "copyright": "Placeholder generated procedurally for Wolfgate",
        "size": {"x": FRAME, "y": FRAME},
        "states": states,
    }

    with open(os.path.join(OUT_DIR, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")

    total_bytes = sum(
        os.path.getsize(os.path.join(OUT_DIR, s["name"] + ".png")) for s in states
    )
    print(f"wrote {len(states)} states to {OUT_DIR} ({total_bytes} bytes of PNG)")


if __name__ == "__main__":
    main()
