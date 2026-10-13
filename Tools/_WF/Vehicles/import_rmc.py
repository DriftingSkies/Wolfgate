#!/usr/bin/env python3
"""Copies the RMC-14 and CMU vehicle sources listed in import_files.yml into the Wolfgate Vehicles module.

The copy is mechanical: the file list and the rewrite rules live in import_files.yml, and nothing outside the list is
touched. For every .cs and .xaml file the script

  * strips the byte order mark and writes the checkout's line endings,
  * rewrites namespaces and the RMCVehicle* renames (the base layer is renamed so it cannot clash with Wolfgate's
    ride-on vehicle system),
  * drops `readonly` from [Dependency] fields and adds `partial` to every type that holds one (and to the types it
    is nested in), which Robust's dependency injection requires,
  * removes duplicate and self-referencing usings left by the namespace merge.

The rewrite gives imported files the flat module namespace (Content.Shared._WF.Vehicles). AGENTS.md wants namespaces to
follow the folder, so a real import (not --dry-run, --out or --list) ends by running fix_namespaces.py over the module,
which moves each file into the namespace of its folder and repairs the usings that named it. --no-fix-namespaces skips
that; run fix_namespaces.py yourself before building.

Stripping RMC-only features (xenos, skills, power loader, ...) is not mechanical and is left to the work package:
the report lists, for each file, the usings that still point at RMC code and a count of the tokens that mark the
systems the game does not have.

    python Tools/_WF/Vehicles/import_rmc.py --list --wp 1          show the plan for work package 1
    python Tools/_WF/Vehicles/import_rmc.py --wp 1                 copy it (never overwrites an existing file)
    python Tools/_WF/Vehicles/import_rmc.py --wp 1 --dry-run       transform and report, write nothing
    python Tools/_WF/Vehicles/import_rmc.py --wp 1 --out scratch   write under scratch/ to look at the result
    python Tools/_WF/Vehicles/import_rmc.py --manifest             regenerate the table in the port manifest

Needs PyYAML (pip install pyyaml).
"""
from __future__ import annotations

import argparse
import re
import sys
from collections import Counter
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover
    sys.exit("import_rmc.py needs PyYAML: pip install pyyaml")

sys.path.insert(0, str(Path(__file__).resolve().parent))
import source_paths  # noqa: E402
from csharp_scan import mask_code  # noqa: E402,F401

REPO = Path(__file__).resolve().parents[3]
CONFIG = Path(__file__).resolve().parent / "import_files.yml"
MANIFEST = "Docs/_WF/Vehicles/port-manifest.md"
MANIFEST_START = "<!-- VEHICLES-ORIGIN START -->"
MANIFEST_END = "<!-- VEHICLES-ORIGIN END -->"
CODE_SUFFIXES = {".cs", ".xaml"}
SYSTEM_BASE_PREFIX = "Content.Shared/Vehicle/"


DEPENDENCY = re.compile(r"\[\s*Dependency\b[^\]]*\]")


def drop_readonly_dependencies(text: str) -> tuple[str, int]:
    """Removes `readonly` from every [Dependency] field; returns the new text and how many fields changed."""
    masked = mask_code(text)
    edits = []
    for match in DEPENDENCY.finditer(masked):
        end = masked.find(";", match.end())
        if end < 0:
            continue
        found = re.search(r"\breadonly\s+", masked[match.end():end])
        if found:
            edits.append((match.end() + found.start(), match.end() + found.end()))
    for start, end in reversed(edits):
        text = text[:start] + text[end:]
    return text, len(edits)


TYPE_KEYWORD = re.compile(r"\b(?:(record)\s+)?(class|struct|interface)\b(?=\s+\w)")


def _body_of(masked: str, after: int) -> tuple[int, int] | None:
    """The brace range of the type declared right before `after`, or None for a declaration without a body."""
    n = len(masked)
    i = after
    parens = 0
    while i < n:
        c = masked[i]
        if c == "(":
            parens += 1
        elif c == ")":
            parens -= 1
        elif parens == 0 and c == ";":
            return None
        elif parens == 0 and c == "{":
            depth = 0
            j = i
            while j < n:
                if masked[j] == "{":
                    depth += 1
                elif masked[j] == "}":
                    depth -= 1
                    if depth == 0:
                        return i, j
                j += 1
            return i, n
        i += 1
    return None


def add_partial_to_dependency_types(text: str) -> tuple[str, int]:
    """Adds `partial` to every type holding a [Dependency] field, and to the types it is nested in."""
    masked = mask_code(text)
    deps = [m.start() for m in DEPENDENCY.finditer(masked)]
    if not deps:
        return text, 0
    inserts = set()
    for match in TYPE_KEYWORD.finditer(masked):
        before = masked[:match.start()].rstrip()
        if before.endswith((":", ",")):
            continue  # a generic constraint, as in `where T : class`
        body = _body_of(masked, match.end())
        if body is None:
            continue
        if not any(body[0] < d < body[1] for d in deps):
            continue
        boundary = max(masked.rfind(c, 0, match.start()) for c in ";{}]")
        if re.search(r"\bpartial\b", masked[boundary + 1:match.start()]):
            continue
        inserts.add(match.start())
    for position in sorted(inserts, reverse=True):
        text = text[:position] + "partial " + text[position:]
    return text, len(inserts)


# ---------------------------------------------------------------------------------------------------------------
# Rewrites
# ---------------------------------------------------------------------------------------------------------------

def _whole(name: str) -> str:
    return r"(?<![\w.])" + re.escape(name) + r"\b"


def rewrite_text(text: str, rel_source: str, rules: dict) -> str:
    """Namespace, qualified-name, type and call rewrites from import_files.yml."""
    for old, new in sorted((rules.get("qualified") or {}).items(), key=lambda kv: -len(kv[0])):
        text = re.sub(_whole(old), new, text)
    for old, new in sorted((rules.get("prefix") or {}).items(), key=lambda kv: -len(kv[0])):
        text = re.sub(_whole(old), new, text)
    for old, new in (rules.get("exact") or {}).items():
        pattern = r"^([ \t]*(?:global[ \t]+)?(?:using[ \t]+(?:static[ \t]+)?|namespace[ \t]+))" + re.escape(old) + r"([ \t]*[;{]|[ \t]*\r?$)"
        text = re.sub(pattern, lambda m: m.group(1) + new + m.group(2), text, flags=re.MULTILINE)
    system = "RMCVehicleSystem" if rel_source.startswith(SYSTEM_BASE_PREFIX) else "VehicleInteriorSystem"
    types = dict(rules.get("types") or {})
    types["VehicleSystem"] = system
    for old, new in types.items():
        text = re.sub(r"(?<![\w])" + re.escape(old) + r"\b", new, text)
    for pattern, replacement in (rules.get("calls") or {}).items():
        text = re.sub(pattern, replacement, text)
    return text


USING = re.compile(r"^using\s+([\w.]+)\s*;[ \t]*\r?\n?", re.MULTILINE)
NAMESPACE = re.compile(r"^namespace\s+([\w.]+)", re.MULTILINE)


def tidy_usings(text: str) -> str:
    """Drops repeated usings and a using of the file's own namespace, both left behind by merging namespaces."""
    found = NAMESPACE.search(text)
    own = found.group(1) if found else None
    seen = set()

    def keep(match: re.Match) -> str:
        name = match.group(1)
        if name == own or name in seen:
            return ""
        seen.add(name)
        return match.group(0)

    return USING.sub(keep, text)


_WOLFGATE_NAMESPACES: set[str] | None = None


def wolfgate_namespaces() -> set[str]:
    """RMC-style namespaces Wolfgate already declares in its small _RMC14 folders; using them is fine."""
    global _WOLFGATE_NAMESPACES
    if _WOLFGATE_NAMESPACES is None:
        found = set()
        for area in ("Content.Shared", "Content.Server", "Content.Client"):
            for path in (REPO / area / "_RMC14").rglob("*.cs"):
                match = NAMESPACE.search(path.read_bytes().decode("utf-8-sig", "replace"))
                if match:
                    found.add(match.group(1))
        _WOLFGATE_NAMESPACES = found
    return _WOLFGATE_NAMESPACES


def external_usings(text: str) -> list[str]:
    """Usings that still point at RMC or CMU code Wolfgate does not have after the rewrite."""
    names = USING.findall(text)
    pattern = r"Content\.\w+\._RMC14\b|Content\.\w+\.CMU14\b|Content\.CMU\b"
    return [n for n in names if re.match(pattern, n) and n not in wolfgate_namespaces()]


def strip_hints(text: str, tokens: list[str]) -> Counter:
    masked = mask_code(text)
    counts = Counter()
    for token in tokens:
        hits = len(re.findall(re.escape(token), masked))
        if hits:
            counts[token] = hits
    return counts


def transform(text: str, rel_source: str, suffix: str, rules: dict) -> tuple[str, dict]:
    """Applies every mechanical change; returns the text (with LF endings) and what changed."""
    text = text.lstrip("﻿")
    text = re.sub(r"\r\n?", "\n", text)
    text = rewrite_text(text, rel_source, rules)
    stats = {"readonly": 0, "partial": 0}
    if suffix == ".cs":
        text = tidy_usings(text)
        text, stats["readonly"] = drop_readonly_dependencies(text)
        text, stats["partial"] = add_partial_to_dependency_types(text)
    return text, stats


# ---------------------------------------------------------------------------------------------------------------
# The plan
# ---------------------------------------------------------------------------------------------------------------

class Item:
    def __init__(self, wp: int, source: str, src: Path, rel_source: str, dest: str):
        self.wp = wp
        self.source = source
        self.src = src
        self.rel_source = rel_source
        self.dest = dest

    @property
    def suffix(self) -> str:
        return Path(self.dest).suffix.lower()


def load_config() -> dict:
    config = yaml.safe_load(CONFIG.read_text(encoding="utf-8"))
    source_paths.resolve(config["sources"], REPO)
    return config


def plan(config: dict, wps: set[int] | None, overrides: dict[str, str]) -> list[Item]:
    items = []
    for group in config["groups"]:
        if wps is not None and group["wp"] not in wps:
            continue
        key = group["source"]
        root = Path(overrides.get(key) or config["sources"][key]["path"])
        for entry in group["files"]:
            if isinstance(entry, dict):
                (name, target), = entry.items()
            else:
                name = target = entry
            rel_source = f"{group['from']}/{name}"
            items.append(Item(group["wp"], key, root / rel_source, rel_source, f"{group['to']}/{target}"))
    return items


def eol() -> str:
    return "\r\n" if sys.platform == "win32" else "\n"


def import_item(item: Item, config: dict, args, newline: str) -> list[str]:
    """Copies one file; returns the report lines for it."""
    lines = []
    target = (Path(args.out) if args.out else REPO) / item.dest
    if not item.src.exists():
        return [f"  MISSING   {item.rel_source}"]
    if target.exists() and not args.force and not args.dry_run:
        return [f"  exists    {item.dest}"]
    if item.suffix in CODE_SUFFIXES:
        raw = item.src.read_bytes().decode("utf-8-sig")
        text, stats = transform(raw, item.rel_source, item.suffix, config["rewrite"])
        data = text.replace("\n", newline).encode("utf-8")
        note = f"readonly -{stats['readonly']}, partial +{stats['partial']}"
        hints = strip_hints(text, config.get("strip_hints") or [])
        externals = external_usings(text) if item.suffix == ".cs" else []
    else:
        data = item.src.read_bytes()
        note = "copied as is"
        hints = Counter()
        externals = []
    verb = "would write" if args.dry_run else "wrote"
    lines.append(f"  {verb:<10}{item.dest}  ({note})")
    if externals:
        lines.append(f"            usings still pointing at RMC/CMU code: {', '.join(sorted(set(externals)))}")
    if hints:
        lines.append("            strip hints: " + ", ".join(f"{k} x{v}" for k, v in hints.most_common()))
    if not args.dry_run:
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    return lines


# ---------------------------------------------------------------------------------------------------------------
# Port manifest
# ---------------------------------------------------------------------------------------------------------------

def manifest_rows(config: dict, root: Path) -> list[tuple[str, str, str, str, str]]:
    sources = config["sources"]
    rows = {}
    for item in plan(config, None, {}):
        if (root / item.dest).exists():
            how = "copied" if item.suffix not in CODE_SUFFIXES else "imported by script"
            rows[item.dest] = (item.dest, item.source, item.rel_source, sources[item.source]["licence"], how)
    for entry in config.get("derived") or []:
        if (root / entry["dest"]).exists():
            how = f"hand-written from source ({entry['note']})"
            rows[entry["dest"]] = (entry["dest"], entry["source"], entry["src"], sources[entry["source"]]["licence"], how)
    return [rows[k] for k in sorted(rows)]


def render_manifest_block(config: dict, root: Path) -> str:
    sources = config["sources"]
    lines = [MANIFEST_START, "<!-- Generated by python Tools/_WF/Vehicles/import_rmc.py --manifest. Don't edit by hand. -->", ""]
    lines.append("| Source | Commit | Date | Licence |")
    lines.append("|---|---|---|---|")
    for key, source in sources.items():
        lines.append(f"| `{key}` | `{source['commit'][:10]}` | {source['date']} | {source['licence']}, {source['copyright']} |")
    lines.append("")
    rows = manifest_rows(config, root)
    if not rows:
        lines.append("No source files have been imported yet.")
    else:
        lines.append("| File | Source | Source path | Licence | How |")
        lines.append("|---|---|---|---|---|")
        for dest, source, src, licence, how in rows:
            lines.append(f"| `{dest}` | `{source}` | `{src}` | {licence} | {how} |")
    lines.append("")
    lines.append(MANIFEST_END)
    return "\n".join(lines)


def write_manifest(config: dict, newline: str) -> None:
    path = REPO / MANIFEST
    block = render_manifest_block(config, REPO)
    if path.exists():
        current = path.read_bytes().decode("utf-8-sig")
        newline = "\r\n" if "\r\n" in current else newline
        current = re.sub(r"\r\n?", "\n", current)
    else:
        path.parent.mkdir(parents=True, exist_ok=True)
        current = "# Vehicles port manifest\n"
    start, end = current.find(MANIFEST_START), current.find(MANIFEST_END)
    if start >= 0 and end > start:
        current = current[:start] + block + current[end + len(MANIFEST_END):]
    else:
        current = current.rstrip() + "\n\n" + block + "\n"
    path.write_bytes(current.replace("\n", newline).encode("utf-8"))
    print(f"manifest: {MANIFEST}")


# ---------------------------------------------------------------------------------------------------------------

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--wp", type=int, action="append", help="work package number; repeat for several")
    parser.add_argument("--all", action="store_true", help="every work package")
    parser.add_argument("--list", action="store_true", help="show the plan and flag missing sources and existing files")
    parser.add_argument("--dry-run", action="store_true", help="transform and report, write nothing")
    parser.add_argument("--out", help="write under this folder instead of the repo")
    parser.add_argument("--force", action="store_true", help="overwrite files that already exist")
    parser.add_argument("--manifest", action="store_true", help="only regenerate the table in the port manifest")
    parser.add_argument("--rmc", help="path of the RMC-14 checkout")
    parser.add_argument("--cmu", help="path of the ColonialMarinesUniverse checkout")
    parser.add_argument("--engine", help="path of RMC-14's RobustToolbox")
    parser.add_argument("--eol", choices=("crlf", "lf"), help="line endings to write (default: the platform's)")
    parser.add_argument("--no-fix-namespaces", action="store_true", help="leave imported files in the flat module namespace")
    args = parser.parse_args(argv)

    config = load_config()
    newline = {"crlf": "\r\n", "lf": "\n"}.get(args.eol or "", eol())
    if args.manifest:
        write_manifest(config, newline)
        return 0
    if not args.wp and not args.all:
        parser.error("name a work package with --wp N, or use --all or --manifest")

    overrides = {key: value for key, value in (("rmc", args.rmc), ("cmu", args.cmu), ("engine", args.engine)) if value}
    items = plan(config, None if args.all else set(args.wp), overrides)
    if not items:
        print("nothing is listed for that work package")
        return 1

    missing = 0
    unresolved = False
    current = None
    for item in items:
        if item.wp != current:
            current = item.wp
            print(f"WP{current}")
        if args.list:
            state = "MISSING" if not item.src.exists() else ("exists" if (REPO / item.dest).exists() else "new")
            missing += state == "MISSING"
            print(f"  {state:<8}{item.rel_source}  ->  {item.dest}")
            continue
        for line in import_item(item, config, args, newline):
            print(line)
            missing += line.lstrip().startswith("MISSING")
    if not args.list and not args.dry_run and not args.out:
        write_manifest(config, newline)
        if not args.no_fix_namespaces:
            import fix_namespaces

            changed, problems = fix_namespaces.fix(REPO)
            print(f"fix_namespaces: {len(changed)} file(s) moved to the namespace of their folder")
            for problem in problems:
                print(f"  problem: {problem}")
            unresolved = bool(problems)
    if missing:
        print(f"{missing} listed source file(s) not found")
    return 1 if missing or unresolved else 0


if __name__ == "__main__":
    sys.exit(main())
