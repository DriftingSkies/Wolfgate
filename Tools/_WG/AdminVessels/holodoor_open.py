"""Draws the holodoor's open, opening and closing states from its closed field.

An open holodoor keeps its emitter frame lit and a faint field between, so the doorway still reads as a holodoor.
Run from the repository root: python Tools/_WG/AdminVessels/holodoor_open.py
"""
from PIL import Image

RSI = "Resources/Textures/_WG/AdminVessels/holodoor.rsi/"
SIZE = 32
FRAME_ALPHA = 0.75
FIELD_ALPHA = 0.22
BORDER = 2


def frames(sheet):
    cols = sheet.width // SIZE
    return [sheet.crop((x * SIZE, y * SIZE, x * SIZE + SIZE, y * SIZE + SIZE))
            for y in range(sheet.height // SIZE) for x in range(cols)]


def sheet(tiles, cols):
    rows = (len(tiles) + cols - 1) // cols
    out = Image.new("RGBA", (cols * SIZE, rows * SIZE))
    for i, tile in enumerate(tiles):
        out.paste(tile, ((i % cols) * SIZE, (i // cols) * SIZE))
    return out


def fade(tile, t):
    """Blends a closed frame toward the open look; t = 0 is closed, 1 is open."""
    out = tile.copy()
    px = out.load()
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b, a = px[x, y]
            edge = min(x, y, SIZE - 1 - x, SIZE - 1 - y) < BORDER
            target = FRAME_ALPHA if edge else FIELD_ALPHA
            px[x, y] = (r, g, b, round(a * (1 - t + t * target)))
    return out


def main():
    closed = frames(Image.open(RSI + "closed.png").convert("RGBA"))
    sheet([fade(f, 1) for f in closed], 4).save(RSI + "open.png")
    sheet([fade(closed[0], t) for t in (0.35, 0.7, 1)], 3).save(RSI + "opening.png")
    sheet([fade(closed[0], t) for t in (0.7, 0.35, 0)], 3).save(RSI + "closing.png")


if __name__ == "__main__":
    main()
