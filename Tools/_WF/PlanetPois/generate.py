"""Build the playable POI grids and their live table entries; --check detects drift."""

import argparse
from collections import Counter, deque
from pathlib import Path

import yaml

from cities import build_maps as cities
from settlements import build_maps as settlements


ROOT = Path(__file__).resolve().parents[3]


def validate(m):
    """Reject overlapping fixtures or doors cut off from the site's road entrance."""
    fixed = Counter((x, y) for _, x, y, _, anchored, _ in m.entities if anchored)
    overlaps = [point for point, count in fixed.items() if count > 1]
    if overlaps:
        raise ValueError(f"{m.slug}: overlapping anchored furnishings: {overlaps}")
    doors = {(x, y) for proto, x, y, *_ in m.entities if proto in ("WoodDoor", "MetalDoor")}
    blocked = set(fixed) - doors
    start = m.sockets[0][:2]
    reached, frontier = set(), deque([start])
    while frontier:
        x, y = frontier.popleft()
        if (x, y) in reached or (x, y) in blocked or (x, y) not in m.tiles:
            continue
        reached.add((x, y))
        frontier.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    required = doors | {(x, y) for x, y, _ in m.sockets}
    if not required <= reached:
        raise ValueError(f"{m.slug}: inaccessible doors or sockets: {required - reached}")
    if not m.roofs or any((x, y) in m.roofs for x, y, _ in m.sockets):
        raise ValueError(f"{m.slug}: buildings need roofs and road mouths must be open to the sky")


def write(path, content, check):
    if check:
        if not path.exists() or path.read_text(encoding="utf-8") != content:
            raise SystemExit(f"Generated content differs: {path.relative_to(ROOT)}")
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="\r\n")


def preview(m, folder):
    """Render a tile and furniture plan without requiring a running game client."""
    from PIL import Image, ImageDraw, ImageFont
    scale = 4 if m.width > 100 else 12
    img = Image.new("RGB", (m.width * scale + 48, m.height * scale + 100), "#15202a")
    draw = ImageDraw.Draw(img)
    colors = {"FloorGrass": "#61734b", "FloorPlanetGrassCrocusNoEdge": "#61734b", "FloorDesert": "#aa9265",
              "FloorAsphalt": "#343b40", "FloorConcreteCrocus": "#9b9f96", "RMCPlanetHybrisaAsphalt": "#343b40",
              "RMCPlanetHybrisaSidewalk": "#9b9f96", "FloorWood": "#9c805e",
              "FloorBrokenWood": "#665647", "FloorConcreteMonoCrocus": "#747f7e", "FloorSnow": "#b5c5c5",
              "FloorSnowDug": "#829ca5", "FloorFlesh": "#713f47", "FloorChromite": "#535560",
              "FloorBasalt": "#514740", "FloorDarkPavement": "#3f4449"}
    for (x, y), tile in m.tiles.items():
        px, py = x * scale + 24, (m.height - 1 - y) * scale + 64
        draw.rectangle((px, py, px + scale - 1, py + scale - 1), fill=colors.get(tile, "#746e5b"))
    for proto, x, y, _, _, _ in m.entities:
        px, py = x * scale + 24, (m.height - 1 - y) * scale + 64
        color = "#d1d4c8" if proto.startswith("Wall") else "#80cdd2" if proto == "Window" else "#d8ac69" if "Door" in proto else "#f1d875" if "Trader" in proto else "#2e5438" if "Tree" in proto else "#292e32"
        inset = 0 if proto.startswith("Wall") or proto == "Window" else max(1, scale // 4)
        draw.rectangle((px + inset, py + inset, px + scale - 1 - inset, py + scale - 1 - inset), fill=color)
    for x, y, _ in m.sockets:
        px, py = x * scale + 24 + scale // 2, (m.height - 1 - y) * scale + 64 + scale // 2
        draw.ellipse((px - 7, py - 7, px + 7, py + 7), outline="#69f2c6", width=3)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 18)
    except OSError:
        font = ImageFont.load_default()
    draw.text((24, 10), m.slug.replace("_", " ").title(), font=font, fill="#f4eedf")
    draw.text((24, 35), f"{m.width} x {m.height} tiles | {m.connection} | {'Occupied / mixed' if m.inhabited else 'Abandoned'}", fill="#c1ced0")
    draw.text((24, img.height - 25), "Tile plan | pale walls / blue windows / gold traders / mint road exits", fill="#c1ced0")
    folder.mkdir(parents=True, exist_ok=True)
    img.save(folder / f"{m.slug}.png")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--preview", type=Path, help="Optional directory for tile-plan PNGs (requires Pillow)")
    args = parser.parse_args()
    maps = cities() + settlements()
    table_path = ROOT / "Resources/Prototypes/_WF/PlanetPois/tables.yml"
    table_text = table_path.read_text(encoding="utf-8")
    tables = yaml.safe_load(table_text)
    # The hand-written header and everything in a table but its authored entries are kept as they are.
    header = "".join(line for line in table_text.splitlines(keepends=True)[:next(
        (i for i, line in enumerate(table_text.splitlines()) if line.strip() and not line.startswith("#")), 0)])
    for m in maps:
        validate(m)
        write(ROOT / "Resources/Maps/_WF/PlanetPois" / f"{m.slug}.yml", m.serialize(), args.check)
        if args.preview:
            preview(m, args.preview)
    # Only the maps built here are replaced; a hand-registered map entry is kept like any other.
    generated = {m.entry()["mapPath"] for m in maps}
    for table in tables:
        planet = table["id"].removeprefix("WFPois")
        authored = [m.entry() for m in maps if m.planet == planet]
        table["entries"] = authored + [entry for entry in table["entries"] if entry.get("mapPath") not in generated]
    write(table_path, header + yaml.safe_dump(tables, sort_keys=False, width=120), args.check)
    print(f"{'Checked' if args.check else 'Generated'} {len(maps)} maps, {sum(len(m.entities) for m in maps):,} mapped entities, six planet tables.")


if __name__ == "__main__":
    main()
