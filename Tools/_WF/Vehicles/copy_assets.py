#!/usr/bin/env python3
"""Copies the Vehicles module's textures, tile textures and audio listed in asset_files.yml and writes their attributions.

    python Tools/_WF/Vehicles/copy_assets.py --list          show what would be copied and flag missing sources
    python Tools/_WF/Vehicles/copy_assets.py                 copy (overwrites a file only when its bytes differ)
    python Tools/_WF/Vehicles/copy_assets.py --table         regenerate the asset table in Docs/_WF/Vehicles/assets.md
    python Tools/_WF/Vehicles/copy_assets.py --paths         print `old new` for every asset path (for rewriting prototypes)
    python Tools/_WF/Vehicles/copy_assets.py --wp 13         only the groups of one work package

An RSI is copied as meta.json plus the PNGs of its states (stray PNGs are dropped). Licence and copyright strings are
never changed, except that a merged RSI takes the copyright of the source that added states to it. Needs PyYAML.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover
    sys.exit("copy_assets.py needs PyYAML: pip install pyyaml")

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import source_paths  # noqa: E402

REPO = HERE.parents[2]
CONFIG = HERE / "asset_files.yml"
IMPORT_CONFIG = HERE / "import_files.yml"
DOC = "Docs/_WF/Vehicles/assets.md"
DOC_START = "<!-- ASSETS START -->"
DOC_END = "<!-- ASSETS END -->"
NEWLINE = "\r\n" if sys.platform == "win32" else "\n"


class Item:
    """One thing to copy: an RSI folder, a merge into an RSI, or a file."""

    def __init__(self, group: dict, src: str, dest: str, keep=None, states=None):
        self.group = group
        self.wp = group["wp"]
        self.kind = group["kind"]
        self.source = group["source"]
        self.src_rel = f"{group['from']}/{src}"
        self.dest_rel = f"{group['to']}/{dest}"
        self.keep = keep
        self.states = states


def load_config() -> dict:
    return yaml.safe_load(CONFIG.read_text(encoding="utf-8"))


def load_sources() -> dict:
    return source_paths.resolve(yaml.safe_load(IMPORT_CONFIG.read_text(encoding="utf-8"))["sources"], REPO)


def source_root(sources: dict, key: str, overrides: dict | None = None) -> Path:
    return Path((overrides or {}).get(key) or sources[key]["path"])


def expand(group: dict, root: Path) -> list[Item]:
    """Turns a group's items into Items; a glob item expands against the source folder."""
    items = []
    for entry in group["items"]:
        if isinstance(entry, str):
            items.append(Item(group, entry, entry))
        elif "glob" in entry:
            for path in sorted((root / group["from"]).glob(entry["glob"])):
                name = path.relative_to(root / group["from"]).as_posix()
                items.append(Item(group, name, name))
        elif "rsi" in entry:
            items.append(Item(group, entry["rsi"], entry["rsi"], states=entry["states"]))
        elif "src" in entry:
            items.append(Item(group, entry["src"], entry.get("dest", entry["src"]), keep=entry.get("keep")))
        else:
            (src, dest), = entry.items()
            items.append(Item(group, src, dest))
    return items


def plan(config: dict, sources: dict, wps: set[int] | None = None, overrides: dict | None = None) -> list[Item]:
    items = []
    for group in config["groups"]:
        if wps is not None and group["wp"] not in wps:
            continue
        items += expand(group, source_root(sources, group["source"], overrides))
    return items


# ---------------------------------------------------------------------------------------------------------------
# RSIs
# ---------------------------------------------------------------------------------------------------------------

def read_json_bytes(data: bytes) -> dict:
    return json.loads(data.decode("utf-8-sig"))


def newline_of(data: bytes) -> str:
    return "\r\n" if b"\r\n" in data else "\n"


def dump_meta(meta: dict, newline: str) -> bytes:
    return (json.dumps(meta, indent=4, ensure_ascii=False).replace("\n", newline) + newline).encode("utf-8")


def write_if_changed(path: Path, data: bytes) -> bool:
    if path.exists() and path.read_bytes() == data:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return True


def copy_rsi(src: Path, dest: Path, keep: list[str] | None) -> tuple[int, list[str]]:
    """Copies meta.json and the state PNGs; returns (files written, problems)."""
    problems = []
    raw = (src / "meta.json").read_bytes()
    meta = read_json_bytes(raw)
    names = [s["name"] for s in meta["states"]]
    if keep is not None:
        unknown = sorted(set(keep) - set(names))
        if unknown:
            problems.append(f"{src}: no state {', '.join(unknown)}")
        meta["states"] = [s for s in meta["states"] if s["name"] in keep]
        names = [s["name"] for s in meta["states"]]
        raw = dump_meta(meta, newline_of(raw))
    written = int(write_if_changed(dest / "meta.json", raw))
    for name in names:
        png = src / f"{name}.png"
        if not png.exists():
            problems.append(f"{src}: state {name} has no PNG")
            continue
        written += int(write_if_changed(dest / png.name, png.read_bytes()))
    stray = sorted(p.name for p in src.glob("*.png") if p.stem not in names) if keep is None else []
    if stray:
        problems.append(f"{src}: not in meta.json, left out: {', '.join(stray)}")
    return written, problems


def already_merged(config: dict, dest_rel: str, dest: Path) -> bool:
    """True when a merge group has already added its states to the RSI at dest, so copying it again would undo them."""
    if not (dest / "meta.json").exists():
        return False
    have = {s["name"] for s in read_json_bytes((dest / "meta.json").read_bytes())["states"]}
    wanted = {s for g in config["groups"] if g["kind"] == "rsi-merge" for i in g["items"]
              if f"{g['to']}/{i['rsi']}" == dest_rel for s in i["states"]}
    return bool(wanted) and wanted <= have


def merge_states(src: Path, dest: Path, states: list[str]) -> tuple[int, list[str]]:
    """Adds states (meta entries and PNGs) from src to the RSI already at dest."""
    problems = []
    if not (dest / "meta.json").exists():
        return 0, [f"{dest}: nothing to merge into; copy it first"]
    dest_raw = (dest / "meta.json").read_bytes()
    dest_meta = read_json_bytes(dest_raw)
    src_meta = read_json_bytes((src / "meta.json").read_bytes())
    if dest_meta["size"] != src_meta["size"]:
        return 0, [f"{src}: frame size {src_meta['size']} differs from {dest_meta['size']}"]
    have = {s["name"] for s in dest_meta["states"]}
    written = 0
    for name in states:
        entry = next((s for s in src_meta["states"] if s["name"] == name), None)
        if entry is None:
            problems.append(f"{src}: no state {name}")
            continue
        if name not in have:
            dest_meta["states"].append(entry)
        written += int(write_if_changed(dest / f"{name}.png", (src / f"{name}.png").read_bytes()))
    src_copyright, dest_copyright = src_meta["copyright"], dest_meta["copyright"]
    if src_copyright != dest_copyright and not dest_copyright.startswith(src_copyright):
        dest_meta["copyright"] = src_copyright if src_copyright.startswith(dest_copyright) else f"{dest_copyright} {src_copyright}"
    written += int(write_if_changed(dest / "meta.json", dump_meta(dest_meta, newline_of(dest_raw))))
    return written, problems


# ---------------------------------------------------------------------------------------------------------------
# Attributions
# ---------------------------------------------------------------------------------------------------------------

def find_source_attribution(file: Path, stop: Path) -> dict | None:
    """The entry for `file` in the nearest attributions.yml at or above it, below `stop`."""
    folder = file.parent
    while True:
        candidate = folder / "attributions.yml"
        if candidate.exists():
            for entry in yaml.safe_load(candidate.read_text(encoding="utf-8-sig")) or []:
                if file.relative_to(folder).as_posix() in entry.get("files", []):
                    return entry
        if folder == stop or folder.parent == folder:
            return None
        folder = folder.parent


def render_attributions(entries: list[dict]) -> str:
    """Entries are dicts with files, license, copyright, source and an optional uncertain flag."""
    blocks = []
    for e in entries:
        lines = []
        if e.get("uncertain"):
            lines.append("# Origin not confirmed; see the copyright line.")
        if len(e["files"]) == 1:
            lines.append(f"- files: [{json.dumps(e['files'][0])}]")
        else:
            lines.append("- files:")
            lines += [f"  - {json.dumps(f)}" for f in e["files"]]
        lines.append(f"  license: {json.dumps(e['license'])}")
        lines.append(f"  copyright: {json.dumps(' '.join(str(e['copyright']).split()), ensure_ascii=False)}")
        lines.append(f"  source: {json.dumps(e['source'])}")
        blocks.append("\n".join(lines))
    return "\n\n".join(blocks) + "\n"


# ---------------------------------------------------------------------------------------------------------------
# Running
# ---------------------------------------------------------------------------------------------------------------

def run(config: dict, sources: dict, wps: set[int] | None, list_only: bool, overrides: dict | None = None) -> int:
    problems = []
    written = 0
    attributions: dict[str, list[dict]] = {}
    for group in config["groups"]:
        if wps is not None and group["wp"] not in wps:
            continue
        root = source_root(sources, group["source"], overrides)
        kind = group["kind"]
        for item in expand(group, root):
            src, dest = root / item.src_rel, REPO / item.dest_rel
            if not src.exists():
                problems.append(f"missing source {item.src_rel}")
                continue
            if list_only:
                print(f"  {item.source}:{item.src_rel} -> {item.dest_rel}")
                continue
            if kind == "rsi":
                if already_merged(config, item.dest_rel, dest):
                    continue
                count, issues = copy_rsi(src, dest, item.keep)
            elif kind == "rsi-merge":
                count, issues = merge_states(src, dest, item.states)
            else:
                count, issues = int(write_if_changed(dest, src.read_bytes())), []
                att_file = group.get("attributions_file")
                if att_file:
                    entry = group.get("attribution")
                    if entry is None:
                        found = find_source_attribution(src, root / "Resources")
                        if found is None:
                            issues.append(f"{item.src_rel}: no attribution in the source and none in the group")
                            entry = {}
                        else:
                            entry = {k: found[k] for k in ("license", "copyright", "source")}
                    rel = Path(item.dest_rel).relative_to(Path(att_file).parent).as_posix()
                    attributions.setdefault(att_file, []).append({"file": rel, **entry})
            written += count
            problems += issues
    for att_file, rows in attributions.items():
        grouped: list[dict] = []
        for row in rows:
            if "license" not in row:
                continue
            key = (row["license"], row["copyright"], row["source"], row.get("uncertain", False))
            existing = next((g for g in grouped if g["key"] == key), None)
            if existing is None:
                grouped.append({"key": key, "files": [row["file"]], "license": row["license"], "copyright": row["copyright"],
                                "source": row["source"], "uncertain": row.get("uncertain", False)})
            else:
                existing["files"].append(row["file"])
        text = render_attributions(grouped).replace("\n", NEWLINE)
        write_if_changed(REPO / att_file, text.encode("utf-8"))
    if not list_only:
        print(f"{written} files written")
    for p in problems:
        print("  PROBLEM", p)
    return 1 if any("left out" not in p for p in problems) else 0


# ---------------------------------------------------------------------------------------------------------------
# Path map and table
# ---------------------------------------------------------------------------------------------------------------

def sprite_path(repo_rel: str) -> str | None:
    """The path a prototype writes for an asset: under Textures for sprites, absolute for audio."""
    for prefix in ("Resources/Textures/", "Resources/Audio/"):
        at = repo_rel.find(prefix)
        if at >= 0:
            rest = repo_rel[at + len(prefix):]
            return rest if prefix.endswith("Textures/") else "/Audio/" + rest
    return None


def path_map(config: dict, sources: dict, overrides: dict | None = None) -> dict[str, str]:
    """Source asset path to Wolfgate asset path, in the form prototypes use (no /Textures prefix for RSIs)."""
    out = {}
    for item in plan(config, sources, None, overrides):
        if item.kind == "rsi-merge":
            continue
        old, new = sprite_path(item.src_rel), sprite_path(item.dest_rel)
        if old and new and not old.endswith("/attributions.yml"):
            out[old] = new
    return out


def table_rows(config: dict, sources: dict, overrides: dict | None = None) -> list[str]:
    lines = ["| Work package | Source | Destination | Licence | Copyright |", "|---|---|---|---|---|"]
    tiles = []
    for item in plan(config, sources, None, overrides):
        dest = REPO / item.dest_rel
        if item.kind == "rsi-merge":
            continue
        licence, copyright_ = "", ""
        if item.kind == "rsi" and (dest / "meta.json").exists():
            meta = json.loads((dest / "meta.json").read_text(encoding="utf-8-sig"))
            licence, copyright_ = meta.get("license", ""), meta.get("copyright", "")
        elif item.kind == "files":
            entry = (item.group.get("attribution") or {})
            if not entry:
                root = source_root(sources, item.source, overrides)
                found = find_source_attribution(root / item.src_rel, root / "Resources")
                entry = found or {}
            licence, copyright_ = entry.get("license", ""), entry.get("copyright", "")
            if "Tiles/vehicle_interior/" in item.dest_rel:
                tiles.append(item)
                continue
        merged = ""
        if item.kind == "rsi":
            extra = [m for m in config["groups"] if m["kind"] == "rsi-merge" and any(
                f"{m['to']}/{i['rsi']}" == item.dest_rel for i in m["items"])]
            if extra:
                merged = " Note: gains states " + ", ".join(s for m in extra for i in m["items"] if f"{m['to']}/{i['rsi']}" == item.dest_rel for s in i["states"]) + " from CMU."
                unnamed = [s for m in extra for i in m["items"] if f"{m['to']}/{i['rsi']}" == item.dest_rel for s in i.get("uncredited", [])]
                if unnamed:
                    merged += " CMU's copyright names no author for " + ", ".join(unnamed) + "."
        if item.keep is not None:
            merged += f" Note: trimmed to {', '.join(item.keep)}."
        copyright_ = " ".join(str(copyright_).split()).replace("|", "\\|")
        lines.append(f"| {item.wp} | `{item.source}:{item.src_rel}` | `{item.dest_rel}` | {licence} | {copyright_}{merged} |")
    if tiles:
        first = tiles[0]
        entry = first.group["attribution"]
        lines.append(f"| {first.wp} | `{first.source}:{first.group['from']}/*.png` ({len(tiles)} files) | `{first.group['to']}/*.png` | "
                     f"{entry['license']} | {' '.join(entry['copyright'].split())} |")
    return lines


def write_doc(config: dict, sources: dict, overrides: dict | None = None) -> None:
    path = REPO / DOC
    block = "\n".join([DOC_START, "<!-- Generated by python Tools/_WF/Vehicles/copy_assets.py --table. Don't edit by hand. -->", ""]
                      + table_rows(config, sources, overrides) + ["", DOC_END])
    current = path.read_bytes().decode("utf-8-sig") if path.exists() else "# Vehicles assets\n"
    current = re.sub(r"\r\n?", "\n", current)
    start, end = current.find(DOC_START), current.find(DOC_END)
    if start >= 0 and end > start:
        current = current[:start] + block + current[end + len(DOC_END):]
    else:
        current = current.rstrip() + "\n\n" + block + "\n"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(current.replace("\n", NEWLINE).encode("utf-8"))
    print(f"table: {DOC}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--wp", type=int, action="append", help="work package number; repeat for several")
    parser.add_argument("--list", action="store_true", help="show the plan, write nothing")
    parser.add_argument("--table", action="store_true", help="regenerate the table in Docs/_WF/Vehicles/assets.md")
    parser.add_argument("--paths", action="store_true", help="print `old new` asset path pairs")
    parser.add_argument("--rmc", help="path of the RMC-14 checkout")
    parser.add_argument("--cmu", help="path of the ColonialMarinesUniverse checkout")
    parser.add_argument("--engine", help="path of RMC-14's RobustToolbox")
    args = parser.parse_args(argv)
    config, sources = load_config(), load_sources()
    overrides = {"rmc": args.rmc, "cmu": args.cmu, "engine": args.engine}
    if args.paths:
        for old, new in sorted(path_map(config, sources, overrides).items()):
            print(f"{old} {new}")
        return 0
    if args.table:
        write_doc(config, sources, overrides)
        return 0
    return run(config, sources, set(args.wp) if args.wp else None, args.list, overrides)


if __name__ == "__main__":
    sys.exit(main())
