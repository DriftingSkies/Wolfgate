"""Draws the lore book's menu icon and its leather and page textures into Resources/Textures/_WF/LoreBook."""
import os
import random

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "..", "Resources", "Textures", "_WF", "LoreBook")


def icon(size=64):
    """A white open book, the same weight as the other 192 dpi menu icons."""
    scale = 4
    s = size * scale
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    w = (255, 255, 255, 255)
    mid = s // 2
    top, bottom = int(s * 0.22), int(s * 0.80)
    left, right = int(s * 0.06), int(s * 0.94)
    sag = int(s * 0.06)
    # Left and right pages, each sagging toward the spine.
    d.polygon([(left, top), (mid - sag // 2, top + sag), (mid - sag // 2, bottom), (left, bottom - sag)], fill=w)
    d.polygon([(right, top), (mid + sag // 2, top + sag), (mid + sag // 2, bottom), (right, bottom - sag)], fill=w)
    # Cut text lines out of each page.
    clear = (0, 0, 0, 0)
    line = int(s * 0.035)
    for i in range(4):
        y = top + int(s * 0.12) + i * int(s * 0.11)
        d.line([(left + int(s * 0.07), y), (mid - int(s * 0.08), y + sag // 2)], fill=clear, width=line)
        d.line([(right - int(s * 0.07), y), (mid + int(s * 0.08), y + sag // 2)], fill=clear, width=line)
    # Cover edge under the pages.
    d.polygon([(left - int(s * 0.03), bottom - sag + int(s * 0.03)), (mid, bottom + int(s * 0.06)),
               (right + int(s * 0.03), bottom - sag + int(s * 0.03)), (right + int(s * 0.03), bottom - sag + int(s * 0.07)),
               (mid, bottom + int(s * 0.10)), (left - int(s * 0.03), bottom - sag + int(s * 0.07))], fill=w)
    return im.resize((size, size), Image.LANCZOS)


def grain(size, base, spread, seed, blur):
    """A tileable noise texture around a base colour."""
    rnd = random.Random(seed)
    im = Image.new("RGB", (size, size))
    px = im.load()
    for y in range(size):
        for x in range(size):
            n = rnd.uniform(-spread, spread)
            px[x, y] = tuple(max(0, min(255, int(c + n))) for c in base)
    # Blur a 3x3 tiling and keep the middle so the edges wrap.
    big = Image.new("RGB", (size * 3, size * 3))
    for ty in range(3):
        for tx in range(3):
            big.paste(im, (tx * size, ty * size))
    big = big.filter(ImageFilter.GaussianBlur(blur))
    return big.crop((size, size, size * 2, size * 2))


def main():
    os.makedirs(OUT, exist_ok=True)
    icon().save(os.path.join(OUT, "lorebook.svg.192dpi.png"))
    grain(128, (59, 36, 22), 26, 1, 1.2).save(os.path.join(OUT, "leather.png"))
    grain(128, (44, 33, 22), 9, 2, 0.8).save(os.path.join(OUT, "page.png"))


if __name__ == "__main__":
    main()
