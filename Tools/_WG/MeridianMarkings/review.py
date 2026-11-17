"""Contact sheets for whoever checks the port: a candidate beside the marking it may duplicate, or on a body."""
import os

from PIL import Image, ImageDraw

import wolfgate as wg

CELL = 80
BACKGROUND = (58, 62, 70, 255)
PANEL = (86, 92, 104, 255)
MARKING_TINT = (232, 106, 44, 255)


def _fit(pictures, box):
    """Crops four facings to one box and scales them to the cell, nearest neighbour."""
    out = []
    width, height = box[2] - box[0], box[3] - box[1]
    scale = max(1, min(CELL // max(width, 1), CELL // max(height, 1)))
    for picture in pictures:
        cropped = picture.crop(box).resize((width * scale, height * scale), Image.NEAREST)
        cell = Image.new("RGBA", (CELL, CELL), PANEL)
        cell.alpha_composite(cropped, ((CELL - cropped.width) // 2, (CELL - cropped.height) // 2))
        out.append(cell)
    return out


def _box(pictures):
    box = None
    for picture in pictures:
        b = picture.getchannel("A").getbbox()
        if b is None:
            continue
        box = b if box is None else (min(box[0], b[0]), min(box[1], b[1]), max(box[2], b[2]), max(box[3], b[3]))
    # Never tighter than the mob's tile, so the art keeps its place on the body.
    cx, cy = wg.CANVAS[0] // 2, wg.CANVAS[1] // 2
    tile = (cx - 16, cy - 16, cx + 16, cy + 16)
    if box is None:
        return tile
    return (min(box[0], tile[0]), min(box[1], tile[1]), max(box[2], tile[2]), max(box[3], tile[3]))


def sheets(rows, out_pattern, per_sheet=14):
    """
    Writes rows of [(label, left pictures, right pictures or None)] as numbered sheets; pictures are
    four facings (south, north, east, west) on the comparison canvas. Returns the paths written.
    """
    paths = []
    for start in range(0, len(rows), per_sheet):
        chunk = rows[start:start + per_sheet]
        sheet = Image.new("RGBA", (CELL * 8 + 24, (CELL + 16) * len(chunk) + 4), BACKGROUND)
        draw = ImageDraw.Draw(sheet)
        for i, (label, left, right) in enumerate(chunk):
            y = i * (CELL + 16) + 2
            draw.text((4, y), label, fill=(240, 240, 240, 255))
            box = _box(list(left) + list(right or []))
            for j, cell in enumerate(_fit(left, box)):
                sheet.alpha_composite(cell, (j * CELL, y + 13))
            if right:
                for j, cell in enumerate(_fit(right, box)):
                    sheet.alpha_composite(cell, (CELL * 4 + 24 + j * CELL, y + 13))
        path = out_pattern % (start // per_sheet + 1)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        sheet.convert("RGB").save(path)
        paths.append(path)
    return paths


def on_body(candidate, bodies, species="Human", sex="Male"):
    """A candidate drawn on a species' body, one image per facing on the comparison canvas."""
    out = []
    for direction in range(4):
        canvas = Image.new("RGBA", wg.CANVAS, (0, 0, 0, 0))

        def paint(sprite):
            frame = sprite.frames[0][direction]
            canvas.alpha_composite(wg.on_canvas(wg.tinted(frame, MARKING_TINT) if sprite.tinted else frame))

        for sprite in candidate.sprites:
            if sprite.behind:
                paint(sprite)
        canvas.alpha_composite(bodies.portrait(species, direction, sex, size=wg.CANVAS))
        for sprite in candidate.sprites:
            if not sprite.behind:
                paint(sprite)
        out.append(canvas)
    return out
