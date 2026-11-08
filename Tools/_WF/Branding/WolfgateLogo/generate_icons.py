"""Generates the Wolfgate window icon set from the 2000x2000 source mark next to this script.

Run from the repository root:  python Tools/_WF/Branding/WolfgateLogo/generate_icons.py
Requires Pillow. Writes icon/icon-NxN.png for every size the client loads plus icon.ico into
Resources/Textures/_WF/Branding/Logo, and wordmark.png (the lettering of logo.png, pre-shrunk for window title
bars). logo.png (splash and main menu) is hand-made and not touched.
"""
import os
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
SOURCE = os.path.join(os.path.dirname(__file__), "Wolfgate Logo.png")
# WFWindowBranding draws it at half this height, so it stays sharp up to 200% UI scale.
WORDMARK_HEIGHT = 18
OUT = os.path.join(ROOT, "Resources", "Textures", "_WF", "Branding", "Logo")

ICON_SIZES = [16, 24, 32, 48, 64, 128, 256]
SS = 8
CLEAR = (0, 0, 0, 0)

written = []


def measure(path):
    """Reads bar colours, the gap-to-bar ratio and the height-to-bar ratio from the source mark."""
    im = Image.open(path).convert("RGBA")
    alpha = im.getchannel("A")
    x0, y0, x1, y1 = alpha.getbbox()
    y = (y0 + y1) // 2
    bars, start = [], None
    for x in range(x0, x1 + 1):
        solid = x < x1 and alpha.getpixel((x, y)) > 127
        if solid and start is None:
            start = x
        elif not solid and start is not None:
            bars.append((start, x))
            start = None
    cx = (bars[0][0] + bars[0][1]) // 2
    rows = sum(1 for yy in range(y0, y1) if alpha.getpixel((cx, yy)) > 127)
    colours = [im.getpixel(((a + b) // 2, y))[:3] for a, b in bars]
    bar_w = sum(b - a for a, b in bars) / len(bars)
    gap = (bars[-1][1] - bars[0][0] - bar_w * len(bars)) / (len(bars) - 1)
    return dict(colours=colours, gap_ratio=gap / bar_w, aspect=rows / bar_w)


def layout(width, m):
    """Integer bar and gap widths that best fill `width`; fractional when nothing crisp fits."""
    n, r = len(m["colours"]), m["gap_ratio"]
    best = None
    for w in range(2, width + 1):
        for g in range(1, w + 1):
            total = n * w + (n - 1) * g
            if total > width:
                break
            score = -abs(g / w - r) / r - 2 * (width - total) / width
            if best is None or score > best[0]:
                best = (score, w, g)
    if best is None:
        w = width / (n + (n - 1) * r)
        return w, w * r
    return best[1], best[2]


def render_icon(size, m):
    """Square window icon: the mark centred, pixel-snapped where the size allows."""
    margin = 0 if size <= 32 else round(size * 0.04)
    w, g = layout(size - 2 * margin, m)
    n = len(m["colours"])
    snapped = isinstance(w, int)
    h = round(w * m["aspect"]) if snapped else w * m["aspect"]
    total = n * w + (n - 1) * g
    x, y = (size - total) / 2, (size - h) / 2
    if snapped:
        x, y = int(x), int(y)
    canvas = Image.new("RGBA", (size * SS, size * SS), CLEAR)
    d = ImageDraw.Draw(canvas)
    for i, c in enumerate(m["colours"]):
        bx = x + i * (w + g)
        d.rectangle(
            [round(bx * SS), round(y * SS), round((bx + w) * SS) - 1, round((y + h) * SS) - 1],
            fill=c + (255,))
    return canvas.reduce(SS)


def render_wordmark(logo):
    """The logo's lettering alone: everything left of the bars, trimmed and shrunk with Lanczos."""
    im = Image.open(logo).convert("RGBA")
    alpha = im.getchannel("A")
    x0, y0, x1, y1 = alpha.getbbox()
    # The widest empty column run separates the lettering from the bars; letters have narrow gaps of their own.
    empty = [not any(alpha.getpixel((x, y)) > 0 for y in range(y0, y1)) for x in range(x0, x1)]
    best, run = (0, 0), None
    for i, blank in enumerate(empty + [False]):
        if blank and run is None:
            run = i
        elif not blank and run is not None:
            best = max(best, (i - run, run))
            run = None
    text = im.crop((x0, y0, x0 + best[1], y1))
    text = text.crop(text.getchannel("A").getbbox())
    width = round(text.width * WORDMARK_HEIGHT / text.height)
    return text.resize((width, WORDMARK_HEIGHT), Image.LANCZOS)


def main():
    m = measure(SOURCE)
    print("bars:", len(m["colours"]), "gap ratio %.3f" % m["gap_ratio"], "aspect %.3f" % m["aspect"])
    os.makedirs(os.path.join(OUT, "icon"), exist_ok=True)

    icons = {s: render_icon(s, m) for s in ICON_SIZES}
    for s, im in icons.items():
        path = os.path.join(OUT, "icon", f"icon-{s}x{s}.png")
        im.save(path, optimize=True)
        written.append(path)

    ico = os.path.join(OUT, "icon.ico")
    icons[256].save(ico, format="ICO", sizes=[(s, s) for s in ICON_SIZES],
                    append_images=[icons[s] for s in ICON_SIZES if s != 256])
    written.append(ico)

    wordmark = os.path.join(OUT, "wordmark.png")
    render_wordmark(os.path.join(OUT, "logo.png")).save(wordmark, optimize=True)
    written.append(wordmark)

    for path in written:
        print("wrote", os.path.relpath(path, ROOT))


if __name__ == "__main__":
    main()
