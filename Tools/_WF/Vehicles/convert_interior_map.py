#!/usr/bin/env python3
"""Converts an RMC-14 or CMU vehicle interior map into a Wolfgate one.

    python Tools/_WF/Vehicles/convert_interior_map.py MAP.yml...              convert into Resources/Maps/_WF/Vehicles/Interiors
    python Tools/_WF/Vehicles/convert_interior_map.py --dry-run MAP.yml...    convert in memory and print the report only
    python Tools/_WF/Vehicles/convert_interior_map.py --rmc-all --cmu-all     every map the port keeps (reads the source checkouts)

Output names are the source names; CMU maps become cmu_<lower-case name>.yml because both repos have a van_interior.yml.

The map is edited as text, line by line, so everything the script does not touch stays byte for byte. It

  * removes the grid blocks that have no Wolfgate meaning (AreaGrid, GridAtmosphere, GasTileOverlay; see `components` in the
    mapping file),
  * renames prototype ids, component names and tile names through interior_map_mapping.yml; a prototype mapped to null is
    deleted from the map together with everything that hangs off it (children, container contents) and its references,
  * drops component fields that no longer exist (`component_fields`),
  * gives the map entity a MapAtmosphere when the mapping file asks for one and the map has none: with no grid atmosphere
    the cabin takes its air from the map, and RMC's maps do not carry any,
  * lays the mapping file's `cabin_tile` under every placed entity (a seat, a marker, a prop) that the source left standing
    over an empty tile, when that tile touches the cabin: the out-of-bounds sweep counts only non-empty tiles as cabin, and
    RMC never had one. A tile that touches nothing is left alone and reported,
  * parents to the grid every entity the source left parented to the map entity (RMC mappers dropped some seats, fittings
    and walls on the map), turning its position into the grid's frame, so that nothing in the interior lacks a GridUid; one
    that stands over no cabin tile even then is out of reach and is deleted,
  * updates entityCount and leaves every other uid alone (the loader only needs them unique),
  * prints every prototype id, component and tile it met that is neither mapped nor present in the Wolfgate tree, so the
    roster packages can fill the mapping file. Ids that a roster package still has to port from an RMC or CMU file under
    the same id are listed as pending, not unmapped.

Needs PyYAML. Reads and writes the file's own newline style; a doubled carriage return is never written.
"""
from __future__ import annotations

import argparse
import base64
import math
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover
    sys.exit("convert_interior_map.py needs PyYAML: pip install pyyaml")

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import source_paths  # noqa: E402

REPO = HERE.parents[2]
MAPPING = HERE / "interior_map_mapping.yml"
IMPORT_CONFIG = HERE / "import_files.yml"
OUT_DIR = "Resources/Maps/_WF/Vehicles/Interiors"
SKIPPED_CMU_MAPS = {"paddywagon.yml"}


# ---------------------------------------------------------------------------------------------------------------
# Map text
# ---------------------------------------------------------------------------------------------------------------

# Where a map file names another entity by uid besides a Transform parent: list fields and single slots.
LIST_KEYS = "ents|buckledEntities"
SLOT_KEYS = "ent|buckledTo"

COMPONENT_LINE = re.compile(r"^    - type: (\S+)\s*$")
UID_LINE = re.compile(r"^  - uid: (\d+)\s*$")
PROTO_LINE = re.compile(r"^- proto: (.*?)\s*$")
TILE_LINE = re.compile(r"^  (\d+): (\S.*?)\s*$")


def unquote(value: str) -> str:
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        return value[1:-1]
    return value


def format_float(value: float) -> str:
    """A position or rotation as the maps write it: seven decimals at most, no trailing `.0`, never `-0`."""
    text = repr(round(value, 7) + 0.0)
    return text[:-2] if text.endswith(".0") else text


class Entity:
    """One `- uid:` item: its lines, with helpers for the component blocks inside."""

    def __init__(self, lines: list[str]):
        self.lines = lines

    @property
    def uid(self) -> int:
        return int(UID_LINE.match(self.lines[0]).group(1))

    def blocks(self) -> list[tuple[str, int, int]]:
        """(component name, first line, one past the last line) of every block under `components:`."""
        out = []
        i = 0
        while i < len(self.lines):
            m = COMPONENT_LINE.match(self.lines[i])
            if not m:
                i += 1
                continue
            j = i + 1
            while j < len(self.lines) and (self.lines[j] == "" or self.lines[j].startswith("     ")):
                j += 1
            out.append((m.group(1), i, j))
            i = j
        return out

    def component_names(self) -> list[str]:
        return [name for name, _, _ in self.blocks()]

    def parent(self) -> int | None:
        for name, start, end in self.blocks():
            if name == "Transform":
                for line in self.lines[start:end]:
                    m = re.match(r"^      parent: (\d+)\s*$", line)
                    if m:
                        return int(m.group(1))
        return None

    def set_placement(self, parent: int, pos: tuple[float, float], rot: float | None) -> None:
        """Rewrites the `parent` and `pos` lines of the Transform block, and the `rot` line when `rot` is given (it is added
        before `pos` if the block has none). Other lines of the block stay as they are."""
        for name, start, end in self.blocks():
            if name != "Transform":
                continue
            body = self.lines[start + 1:end]
            has_rot = any(re.match(r"^      rot: ", line) for line in body)
            has_pos = any(re.match(r"^      pos: ", line) for line in body)
            pos_line = f"      pos: {format_float(pos[0])},{format_float(pos[1])}"
            rot_line = f"      rot: {format_float(rot)} rad" if rot is not None else None
            out: list[str] = []
            for line in body:
                if re.match(r"^      parent: \d+\s*$", line):
                    if not has_pos:
                        if rot_line and not has_rot:
                            out.append(rot_line)
                        out.append(pos_line)
                    out.append(f"      parent: {parent}")
                elif re.match(r"^      pos: ", line):
                    if rot_line and not has_rot:
                        out.append(rot_line)
                    out.append(pos_line)
                elif re.match(r"^      rot: ", line):
                    out.append(rot_line or line)
                else:
                    out.append(line)
            self.lines[start + 1:end] = out
            return

    def missing_components(self) -> tuple[int, list[tuple[int, str]]] | None:
        """(line of `missingComponents:`, [(line, name), ...]) or None."""
        for i, line in enumerate(self.lines):
            if line == "    missingComponents:":
                items = []
                j = i + 1
                while j < len(self.lines) and re.match(r"^    - \S+\s*$", self.lines[j]):
                    items.append((j, self.lines[j][6:].strip()))
                    j += 1
                return i, items
        return None

    def add_component(self, block: list[str]) -> None:
        """Appends a component block after the last one, before `missingComponents:` if the entity has that."""
        at = len(self.lines)
        for i, line in enumerate(self.lines):
            if line == "    missingComponents:":
                at = i
                break
        else:
            while at > 0 and self.lines[at - 1] == "":
                at -= 1
        self.lines[at:at] = block

    def remove_lines(self, indexes: set[int]) -> None:
        self.lines = [l for i, l in enumerate(self.lines) if i not in indexes]

    def remove_component(self, name: str) -> int:
        """Removes every block of that component and its entry in missingComponents; returns how many blocks went."""
        removed = 0
        drop: set[int] = set()
        for n, start, end in self.blocks():
            if n == name:
                drop.update(range(start, end))
                removed += 1
        missing = self.missing_components()
        if missing:
            header, items = missing
            hit = [line for line, n in items if n == name]
            drop.update(hit)
            if hit and len(hit) == len(items):
                drop.add(header)
        if drop:
            self.remove_lines(drop)
            self._drop_empty_components_header()
        return removed

    def _drop_empty_components_header(self) -> None:
        for i, line in enumerate(self.lines):
            if line == "    components:" and (i + 1 >= len(self.lines) or not (
                    COMPONENT_LINE.match(self.lines[i + 1]) or self.lines[i + 1].startswith("      "))):
                del self.lines[i]
                return

    def rename_component(self, old: str, new: str) -> int:
        count = 0
        for i, line in enumerate(self.lines):
            if COMPONENT_LINE.match(line) and line.strip() == f"- type: {old}":
                self.lines[i] = f"    - type: {new}"
                count += 1
        missing = self.missing_components()
        if missing:
            for line, n in missing[1]:
                if n == old:
                    self.lines[line] = f"    - {new}"
        return count

    def remove_field(self, component: str, field_name: str) -> int:
        """Removes `field_name:` and everything nested under it from the component's blocks."""
        removed = 0
        for name, start, end in reversed(self.blocks()):
            if name != component:
                continue
            i = start + 1
            while i < end:
                if re.match(rf"^      {re.escape(field_name)}:(\s|$)", self.lines[i]):
                    j = i + 1
                    while j < end and (self.lines[j].startswith("       ") or self.lines[j] == ""):
                        j += 1
                    del self.lines[i:j]
                    end -= j - i
                    removed += 1
                else:
                    i += 1
        return removed

    def drop_references(self, gone: set[int]) -> int:
        """Removes `gone` uids from entity lists (container `ents`, strap `buckledEntities`) and nulls entity slots
        (`ent`, `buckledTo`); returns how many references went."""
        count = 0
        out: list[str] = []
        i = 0
        lines = self.lines
        while i < len(lines):
            line = lines[i]
            inline = re.match(rf"^(\s*)({LIST_KEYS}): \[(.*)\]\s*$", line)
            if inline:
                values = [v.strip() for v in inline.group(3).split(",") if v.strip()]
                keep = [v for v in values if int(v) not in gone]
                count += len(values) - len(keep)
                out.append(f"{inline.group(1)}{inline.group(2)}: [{', '.join(keep)}]" if keep else f"{inline.group(1)}{inline.group(2)}: []")
                i += 1
                continue
            block = re.match(rf"^(\s*)({LIST_KEYS}):\s*$", line)
            if block:
                indent = block.group(1)
                j = i + 1
                items = []
                while j < len(lines) and re.match(rf"^{indent}- (\d+)\s*$", lines[j]):
                    items.append(lines[j])
                    j += 1
                keep = [l for l in items if int(l.strip()[2:]) not in gone]
                count += len(items) - len(keep)
                out.append(line if keep or not items else f"{indent}{block.group(2)}: []")
                if keep or not items:
                    out.extend(keep)
                i = j
                continue
            slot = re.match(rf"^(\s*)({SLOT_KEYS}): (\d+)\s*$", line)
            if slot and int(slot.group(3)) in gone:
                out.append(f"{slot.group(1)}{slot.group(2)}: null")
                count += 1
                i += 1
                continue
            out.append(line)
            i += 1
        self.lines = out
        return count


@dataclass
class Group:
    proto: str
    header: list[str]
    entities: list[Entity] = field(default_factory=list)


class MapDoc:
    """A map file split into the head (up to `entities:`), the prototype groups and a tail (such as a closing `...`)."""

    def __init__(self, text: str):
        self.newline = "\r\n" if text.count("\r\n") > text.count("\n") / 2 else "\n"
        text = text.lstrip("﻿")
        text = re.sub(r"\r+\n", "\n", text).replace("\r", "\n")
        lines = text.split("\n")
        try:
            at = lines.index("entities:")
        except ValueError:
            raise ValueError("not a map file: no `entities:` line")
        self.head = lines[:at + 1]
        self.groups: list[Group] = []
        self.tail: list[str] = []
        group: Group | None = None
        entity: list[str] | None = None
        for line in lines[at + 1:]:
            proto = PROTO_LINE.match(line)
            if proto:
                group = Group(unquote(proto.group(1)), [line])
                self.groups.append(group)
                entity = None
            elif group is None:
                self.tail.append(line)
            elif UID_LINE.match(line):
                entity = [line]
                group.entities.append(Entity(entity))
            elif entity is not None and (line.startswith("    ") or line == ""):
                entity.append(line)
            elif entity is None and line.startswith("  "):
                group.header.append(line)
            else:
                group = None
                entity = None
                self.tail.append(line)
        self._fix_trailing_blank()

    def _fix_trailing_blank(self) -> None:
        """The final empty string of a file that ends in a newline lands in the last entity; move it to the tail."""
        if self.groups and self.groups[-1].entities:
            last = self.groups[-1].entities[-1].lines
            trailing = []
            while last and last[-1] == "":
                trailing.append(last.pop())
            self.tail = trailing + self.tail

    def entities(self):
        for group in self.groups:
            for entity in group.entities:
                yield group, entity

    def tilemap(self) -> dict[int, str]:
        out = {}
        reading = False
        for line in self.head:
            if line == "tilemap:":
                reading = True
                continue
            if reading:
                m = TILE_LINE.match(line)
                if not m:
                    break
                out[int(m.group(1))] = m.group(2)
        return out

    def set_tile_name(self, tile_id: int, name: str) -> None:
        for i, line in enumerate(self.head):
            m = TILE_LINE.match(line)
            if m and int(m.group(1)) == tile_id:
                self.head[i] = f"  {tile_id}: {name}"
                return

    def add_tile_name(self, name: str) -> int:
        """Appends `name` to the tilemap under the next free id and returns the id."""
        tiles = self.tilemap()
        new_id = max(tiles) + 1 if tiles else 0
        last = None
        reading = False
        for i, line in enumerate(self.head):
            if line == "tilemap:":
                reading = True
                last = i
            elif reading and TILE_LINE.match(line):
                last = i
            elif reading:
                break
        self.head.insert(last + 1, f"  {new_id}: {name}")
        return new_id

    def set_entity_count(self, count: int) -> None:
        for i, line in enumerate(self.head):
            if re.match(r"^  entityCount: \d+\s*$", line):
                self.head[i] = f"  entityCount: {count}"

    def entity_count(self) -> int:
        return sum(len(g.entities) for g in self.groups)

    def merge_groups(self) -> None:
        merged: dict[str, Group] = {}
        order: list[Group] = []
        for group in self.groups:
            if not group.entities:
                continue
            if group.proto in merged:
                merged[group.proto].entities.extend(group.entities)
            else:
                merged[group.proto] = group
                order.append(group)
        self.groups = order

    def render(self) -> str:
        lines = list(self.head)
        for group in self.groups:
            lines.extend(group.header)
            for entity in group.entities:
                lines.extend(entity.lines)
        lines.extend(self.tail)
        return self.newline.join(lines)


# ---------------------------------------------------------------------------------------------------------------
# What exists in the Wolfgate tree
# ---------------------------------------------------------------------------------------------------------------

def scan_prototype_ids(root: Path) -> dict[str, set[str]]:
    """Ids of entity and tile prototypes defined in the YAML under root: {"entity": {...}, "tile": {...}}."""
    found: dict[str, set[str]] = {"entity": set(), "tile": set()}
    files = [root] if root.is_file() else sorted(root.rglob("*.yml")) if root.exists() else []
    for path in files:
        try:
            text = path.read_text(encoding="utf-8-sig")
        except (OSError, UnicodeDecodeError):
            continue
        kind, ident = None, None
        for line in text.splitlines() + ["- end"]:
            if line.startswith("- "):
                if kind in found and ident:
                    found[kind].add(ident)
                kind, ident = None, None
            m = re.match(r"^(?:- |  )type: (\S+)\s*$", line)
            if m:
                kind = m.group(1)
            m = re.match(r"^(?:- |  )id: (\S+)\s*$", line)
            if m:
                ident = unquote(m.group(1))
    return found


CLASS_LINE = re.compile(r"\bclass (\w+)")
PROTO_NAME = re.compile(r'ComponentProtoName\("([^"]+)"\)')


def component_names_in(text: str) -> set[str]:
    """Names registered by `[RegisterComponent]` classes in a C# file (the class name minus Component, or the
    ComponentProtoName)."""
    names: set[str] = set()
    lines = text.split("\n")
    for i, line in enumerate(lines):
        if "RegisterComponent" not in line or not line.lstrip().startswith("["):
            continue
        attrs = []
        k = i - 1
        while k >= 0 and lines[k].lstrip().startswith("["):
            attrs.append(lines[k])
            k -= 1
        for j in range(i, min(len(lines), i + 12)):
            attrs.append(lines[j])
            m = CLASS_LINE.search(lines[j])
            if m:
                custom = PROTO_NAME.search("\n".join(attrs))
                cls = m.group(1)
                names.add(custom.group(1) if custom else cls[:-len("Component")] if cls.endswith("Component") else cls)
                break
    return names


def scan_components(root: Path) -> set[str]:
    """Registered component names in the C# under root."""
    names: set[str] = set()
    if not root.exists():
        return names
    for path in root.rglob("*.cs"):
        try:
            text = path.read_text(encoding="utf-8-sig")
        except (OSError, UnicodeDecodeError):
            continue
        if "RegisterComponent" in text:
            names |= component_names_in(text)
    return names


class Knowledge:
    """What the Wolfgate tree has, and what the roster packages will add under the same id."""

    def __init__(self, repo: Path, mapping: dict, sources: dict, overrides: dict | None = None):
        ids = scan_prototype_ids(repo / "Resources" / "Prototypes")
        self.protos = ids["entity"]
        self.tiles = ids["tile"]
        self.components = set(mapping.get("known_engine_components") or [])
        for sub in ("Content.Shared", "Content.Server", "Content.Client"):
            self.components |= scan_components(repo / sub)
        for extra in mapping.get("engine_roots") or []:
            self.components |= scan_components(repo / extra)
        self.pending_protos: dict[str, str] = {}
        self.pending_components: dict[str, str] = {}
        renames = mapping.get("components") or {}
        for entry in mapping.get("provided_by") or []:
            root = Path((overrides or {}).get(entry["source"]) or sources[entry["source"]]["path"])
            label = entry.get("note") or "listed in the mapping file"
            for rel in entry.get("paths") or []:
                for ident in scan_prototype_ids(root / rel)["entity"]:
                    self.pending_protos.setdefault(ident, f"{entry['source']}:{rel}")
            for ident in entry.get("ids") or []:
                self.pending_protos.setdefault(ident, f"{entry['source']} ({label})")
            for rel in entry.get("component_roots") or []:
                for name in scan_components(root / rel):
                    new = renames.get(name)
                    self.pending_components.setdefault(new if isinstance(new, str) else name, f"{entry['source']}:{rel}")


# ---------------------------------------------------------------------------------------------------------------
# Conversion
# ---------------------------------------------------------------------------------------------------------------

class Report:
    def __init__(self):
        self.maps = 0
        self.entities_before = 0
        self.entities_after = 0
        self.removed: Counter = Counter()
        self.renamed: Counter = Counter()
        self.dropped_blocks: Counter = Counter()
        self.dropped_fields: Counter = Counter()
        self.atmospheres = 0
        self.refs_dropped = 0
        self.grounded: Counter = Counter()
        self.grounded_tiles = 0
        self.not_grounded: list[str] = []
        self.adopted: Counter = Counter()
        self.strays: Counter = Counter()
        self.unmapped: dict[str, Counter] = {"prototype": Counter(), "component": Counter(), "tile": Counter()}
        self.pending: dict[str, dict[str, tuple[str, Counter]]] = {"prototype": {}, "component": {}, "tile": {}}
        self.where: dict[tuple[str, str], set[str]] = defaultdict(set)
        self.bad_targets: dict[str, set[str]] = defaultdict(set)

    def see(self, kind: str, ident: str, map_name: str, count: int) -> None:
        self.where[(kind, ident)].add(map_name)

    def lines(self) -> list[str]:
        out = [f"{self.maps} map(s): {self.entities_before} entities in, {self.entities_after} out"]
        if self.removed:
            out.append("deleted placements (mapped to null, with their children): " + ", ".join(f"{k} x{v}" for k, v in sorted(self.removed.items())))
        if self.renamed:
            out.append("renamed: " + ", ".join(f"{k} x{v}" for k, v in sorted(self.renamed.items())))
        if self.dropped_blocks:
            out.append("component blocks dropped: " + ", ".join(f"{k} x{v}" for k, v in sorted(self.dropped_blocks.items())))
        if self.dropped_fields:
            out.append("component fields dropped: " + ", ".join(f"{k} x{v}" for k, v in sorted(self.dropped_fields.items())))
        if self.atmospheres:
            out.append(f"MapAtmosphere added to {self.atmospheres} map entity(ies)")
        if self.refs_dropped:
            out.append(f"references to deleted entities removed: {self.refs_dropped}")
        if self.grounded:
            out.append(f"cabin tile laid under {self.grounded_tiles} tile(s) the source left empty beneath: "
                       + ", ".join(f"{k} x{v}" for k, v in sorted(self.grounded.items())))
        for note in self.not_grounded:
            out.append(f"NOT grounded: {note}")
        if self.adopted:
            out.append("parented to the grid instead of the map: " + ", ".join(f"{k} x{v}" for k, v in sorted(self.adopted.items())))
        if self.strays:
            out.append("deleted, stood on the map over no cabin tile: " + ", ".join(f"{k} x{v}" for k, v in sorted(self.strays.items())))
        for kind, counter in self.unmapped.items():
            out.append(f"UNMAPPED {kind}s ({len(counter)}):")
            for ident, n in sorted(counter.items()):
                out.append(f"  {ident}  x{n}  in {', '.join(sorted(self.where[(kind, ident)]))}")
        for kind, table in self.pending.items():
            out.append(f"pending {kind}s, to come from a roster package ({len(table)}):")
            for ident, (src, counter) in sorted(table.items()):
                out.append(f"  {ident}  x{sum(counter.values())}  <- {src}")
        if self.bad_targets:
            out.append("mapped to an id that is not in the Wolfgate tree and not pending:")
            for target, sources_ in sorted(self.bad_targets.items()):
                out.append(f"  {target}  (from {', '.join(sorted(sources_))})")
        return out


def atmosphere_block(spec: dict) -> list[str]:
    """The MapAtmosphere component block for a map entity: an immutable mixture of `moles` (padded with zeros to
    `gas_count` entries) at the given volume and temperature."""
    moles = list(spec.get("moles") or [])
    moles += [0] * (int(spec.get("gas_count", 12)) - len(moles))
    lines = [
        "    - type: MapAtmosphere",
        "      space: False",
        "      mixture:",
        f"        volume: {spec.get('volume', 2500)}",
        "        immutable: True",
        f"        temperature: {spec.get('temperature', 293.15)}",
        "        moles:",
    ]
    lines += [f"        - {m}" for m in moles]
    return lines


def add_map_atmosphere(doc: MapDoc, spec: dict, report: Report) -> None:
    for _, entity in doc.entities():
        names = entity.component_names()
        if "Map" not in names:
            continue
        if "MapAtmosphere" not in names:
            entity.add_component(atmosphere_block(spec))
            report.atmospheres += 1
        return


TILE_BYTES = 7
CHUNK_SIZE = 16
EMPTY_TILE = "Space"
CHUNK_IND_LINE = re.compile(r"^          ind: (-?\d+),(-?\d+)\s*$")
CHUNK_TILES_LINE = re.compile(r"^          tiles: (\S+)\s*$")
CHUNK_VERSION_LINE = re.compile(r"^          version: (\d+)\s*$")
POS_LINE = re.compile(r"^      pos: (\S+?),(\S+)\s*$")
ROT_LINE = re.compile(r"^      rot: (\S+) rad\s*$")
PARENT_LINE = re.compile(r"^      parent: (\d+)\s*$")


class GridTiles:
    """The tiles of a map's grid, read from the version 7 chunk lines of its entity and written back to them."""

    def __init__(self, entity: Entity, names: dict[int, str]):
        self.entity = entity
        self.names = names
        self.chunks: dict[tuple[int, int], tuple[int, bytearray]] = {}
        self.readable = True
        lines = entity.lines
        for i, line in enumerate(lines):
            ind = CHUNK_IND_LINE.match(line)
            if not ind:
                continue
            tiles = CHUNK_TILES_LINE.match(lines[i + 1]) if i + 1 < len(lines) else None
            version = CHUNK_VERSION_LINE.match(lines[i + 2]) if i + 2 < len(lines) else None
            data = bytearray(base64.b64decode(tiles.group(1))) if tiles else bytearray()
            if not version or version.group(1) != "7" or len(data) != CHUNK_SIZE * CHUNK_SIZE * TILE_BYTES:
                self.readable = False
                return
            self.chunks[(int(ind.group(1)), int(ind.group(2)))] = (i + 1, data)

    def _locate(self, tile: tuple[int, int]):
        chunk = (tile[0] // CHUNK_SIZE, tile[1] // CHUNK_SIZE)
        found = self.chunks.get(chunk)
        if found is None:
            return None
        x, y = tile[0] - chunk[0] * CHUNK_SIZE, tile[1] - chunk[1] * CHUNK_SIZE
        return found, (y * CHUNK_SIZE + x) * TILE_BYTES

    def name(self, tile: tuple[int, int]) -> str | None:
        """The tile's name, or None where the grid has no chunk."""
        located = self._locate(tile)
        if located is None:
            return None
        (_, data), at = located
        return self.names.get(int.from_bytes(data[at:at + 4], "little", signed=True), "?")

    def is_cabin(self, tile: tuple[int, int]) -> bool:
        name = self.name(tile)
        return name is not None and name != EMPTY_TILE

    def set(self, tile: tuple[int, int], tile_id: int) -> None:
        (line, data), at = self._locate(tile)
        data[at:at + TILE_BYTES] = tile_id.to_bytes(4, "little", signed=True) + bytes(TILE_BYTES - 4)
        self.entity.lines[line] = "          tiles: " + base64.b64encode(bytes(data)).decode("ascii")


def placement(entity: Entity) -> tuple[int | None, tuple[float, float], float]:
    """(parent uid, position, rotation) from the entity's Transform block; position and rotation default to zero."""
    parent, pos, rot = None, (0.0, 0.0), 0.0
    for name, start, end in entity.blocks():
        if name != "Transform":
            continue
        for line in entity.lines[start:end]:
            m = PARENT_LINE.match(line)
            if m:
                parent = int(m.group(1))
            m = POS_LINE.match(line)
            if m:
                pos = (float(m.group(1)), float(m.group(2)))
            m = ROT_LINE.match(line)
            if m:
                rot = float(m.group(1))
    return parent, pos, rot


def ground_placed_entities(doc: MapDoc, cabin_tile: str, report: Report, map_name: str) -> None:
    """Lays `cabin_tile` under every entity placed on the grid or on the map entity over an empty tile, when that tile
    touches the cabin (a tile this already laid counts). Entities in containers have another parent and are skipped."""
    grid = map_entity = None
    for _, entity in doc.entities():
        names = entity.component_names()
        if "MapGrid" in names and grid is None:
            grid = entity
        if "Map" in names and map_entity is None:
            map_entity = entity
    if grid is None:
        return
    tile_names = doc.tilemap()
    tiles = GridTiles(grid, tile_names)
    if not tiles.readable:
        report.not_grounded.append(f"{map_name}: the grid's chunks are not the version 7, 16 by 16 form this reads")
        return
    _, grid_pos, grid_rot = placement(grid)
    cos, sin = math.cos(grid_rot), math.sin(grid_rot)

    wanted: dict[tuple[int, int], list[str]] = defaultdict(list)
    for group, entity in doc.entities():
        if entity is grid or entity is map_entity:
            continue
        parent, pos, _ = placement(entity)
        if parent == grid.uid:
            local = pos
        elif map_entity is not None and parent == map_entity.uid:
            dx, dy = pos[0] - grid_pos[0], pos[1] - grid_pos[1]
            local = (dx * cos + dy * sin, -dx * sin + dy * cos)
        else:
            continue
        wanted[(math.floor(local[0]), math.floor(local[1]))].append(group.proto)

    todo = {tile for tile in wanted if tiles.name(tile) == EMPTY_TILE}
    cabin_id = next((i for i, n in tile_names.items() if n == cabin_tile), None)
    laid = True
    while laid:
        laid = False
        for tile in sorted(todo):
            if not any(tiles.is_cabin((tile[0] + dx, tile[1] + dy)) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                continue
            if cabin_id is None:
                cabin_id = doc.add_tile_name(cabin_tile)
                tile_names[cabin_id] = cabin_tile
            tiles.set(tile, cabin_id)
            todo.discard(tile)
            report.grounded_tiles += 1
            for proto in wanted[tile]:
                report.grounded[proto] += 1
            laid = True
    for tile in sorted(todo):
        report.not_grounded.append(f"{map_name}: tile {tile[0]},{tile[1]} under {', '.join(sorted(set(wanted[tile])))} "
                                   "is empty and touches no cabin tile")
    for tile in sorted(t for t in wanted if tiles.name(t) is None):
        report.not_grounded.append(f"{map_name}: tile {tile[0]},{tile[1]} under {', '.join(sorted(set(wanted[tile])))} "
                                   "is in a chunk the grid does not have")


def adopt_map_parented(doc: MapDoc, report: Report, map_name: str) -> None:
    """Parents to the grid every entity the source left parented to the map entity over a cabin tile, turning its position
    (and rotation, for a turned grid) into the grid's frame. The loader keeps a child of the map where it is, so it has no
    GridUid and is not anchored, and nothing that walks the grid's children (anchoring, cleanup, ejection) sees it. One that
    stands over no cabin tile is out of reach of everyone inside, takes no part in the cabin and is deleted with what hangs
    off it. Run after the cabin tile is laid, so the tiles it needs are there."""
    grid = map_entity = None
    for _, entity in doc.entities():
        names = entity.component_names()
        if "MapGrid" in names and grid is None:
            grid = entity
        if "Map" in names and map_entity is None:
            map_entity = entity
    if grid is None or map_entity is None:
        return
    tiles = GridTiles(grid, doc.tilemap())
    if not tiles.readable:
        report.not_grounded.append(f"{map_name}: the grid's chunks are not the version 7, 16 by 16 form this reads, so what "
                                   "is parented to the map stays there")
        return
    _, grid_pos, grid_rot = placement(grid)
    cos, sin = math.cos(grid_rot), math.sin(grid_rot)

    strays: set[int] = set()
    for group, entity in doc.entities():
        if entity is grid or entity is map_entity:
            continue
        parent, pos, rot = placement(entity)
        if parent != map_entity.uid:
            continue
        dx, dy = pos[0] - grid_pos[0], pos[1] - grid_pos[1]
        local = (dx * cos + dy * sin, -dx * sin + dy * cos)
        if tiles.is_cabin((math.floor(local[0]), math.floor(local[1]))):
            entity.set_placement(grid.uid, local, rot - grid_rot if grid_rot else None)
            report.adopted[group.proto] += 1
        else:
            strays.add(entity.uid)
            report.strays[group.proto] += 1
    if strays:
        remove_entities(doc, strays, report)


def remove_entities(doc: MapDoc, direct: set[int], report: Report) -> None:
    """Deletes the entities in `direct`, the children and container contents that hang off them, and every reference to any
    of them. The caller counts the entities in `direct`; the others are counted here."""
    doomed = set(direct)
    parents = {e.uid: e.parent() for _, e in doc.entities()}
    grew = True
    while grew:
        grew = False
        for uid, parent in parents.items():
            if parent in doomed and uid not in doomed:
                doomed.add(uid)
                grew = True
    if not doomed:
        return
    for group in doc.groups:
        keep = []
        for entity in group.entities:
            if entity.uid not in doomed:
                keep.append(entity)
            elif entity.uid not in direct:
                report.removed[f"{group.proto or '(unnamed)'} (child of a deleted entity)"] += 1
        group.entities = keep
    for _, entity in doc.entities():
        report.refs_dropped += entity.drop_references(doomed)


def convert(doc: MapDoc, mapping: dict, know: Knowledge, report: Report, map_name: str) -> None:
    report.maps += 1
    report.entities_before += doc.entity_count()
    protos = dict(mapping.get("prototypes") or {})
    protos.update((mapping.get("map_prototypes") or {}).get(map_name) or {})
    comps = mapping.get("components") or {}
    fields = mapping.get("component_fields") or {}
    tiles = mapping.get("tiles") or {}

    # Prototypes: rename, or mark for deletion.
    direct: set[int] = set()
    for group in doc.groups:
        if group.proto == "":
            continue
        if group.proto in protos:
            target = protos[group.proto]
            if target is None:
                direct.update(e.uid for e in group.entities)
                report.removed[group.proto] += len(group.entities)
                continue
            report.renamed[f"{group.proto} -> {target}"] += len(group.entities)
            if target not in know.protos and target not in know.pending_protos:
                report.bad_targets[target].add(group.proto)
            group.proto = target
            group.header[0] = f"- proto: {target}"
        elif group.proto in know.protos:
            report.see("prototype", group.proto, map_name, len(group.entities))
        elif group.proto in know.pending_protos:
            table = report.pending["prototype"].setdefault(group.proto, (know.pending_protos[group.proto], Counter()))
            table[1][map_name] += len(group.entities)
        else:
            report.unmapped["prototype"][group.proto] += len(group.entities)
            report.where[("prototype", group.proto)].add(map_name)

    remove_entities(doc, direct, report)

    # Components: rename, drop blocks, drop fields.
    seen_components: Counter = Counter()
    for _, entity in doc.entities():
        for name in set(entity.component_names()):
            seen_components[name] += 1
            if name in comps:
                target = comps[name]
                if target is None:
                    report.dropped_blocks[name] += entity.remove_component(name)
                else:
                    entity.rename_component(name, target)
                    report.renamed[f"component {name} -> {target}"] += 1
                    if target not in know.components and target not in know.pending_components:
                        report.bad_targets[target].add(f"component {name}")
        missing = entity.missing_components()
        if missing:
            for _, name in missing[1]:
                if name in comps and comps[name] is None:
                    entity.remove_component(name)
                elif name in comps:
                    entity.rename_component(name, comps[name])
        for component, names in fields.items():
            for field_name in names:
                n = entity.remove_field(component, field_name)
                if n:
                    report.dropped_fields[f"{component}.{field_name}"] += n
    for name, count in seen_components.items():
        if name in comps:
            continue
        if name in know.components:
            continue
        if name in know.pending_components:
            table = report.pending["component"].setdefault(name, (know.pending_components[name], Counter()))
            table[1][map_name] += count
        else:
            report.unmapped["component"][name] += count
            report.where[("component", name)].add(map_name)

    # Tiles. A tile mapped to null becomes Space.
    for tile_id, name in doc.tilemap().items():
        if name in tiles:
            target = tiles[name] or "Space"
            doc.set_tile_name(tile_id, target)
            report.renamed[f"tile {name} -> {target}"] += 1
            if target not in know.tiles:
                report.bad_targets[target].add(f"tile {name}")
        elif name in know.tiles:
            continue
        else:
            report.unmapped["tile"][name] += 1
            report.where[("tile", name)].add(map_name)

    if mapping.get("cabin_tile"):
        ground_placed_entities(doc, mapping["cabin_tile"], report, map_name)
        adopt_map_parented(doc, report, map_name)

    if mapping.get("map_atmosphere"):
        add_map_atmosphere(doc, mapping["map_atmosphere"], report)

    doc.merge_groups()
    doc.set_entity_count(doc.entity_count())
    report.entities_after += doc.entity_count()


def verify(doc: MapDoc) -> list[str]:
    """Structural problems left in a converted map: duplicate uids, parents or container contents that point at an entity
    that is not there, and an entityCount that does not match."""
    problems = []
    uids = [e.uid for _, e in doc.entities()]
    present = set(uids)
    for uid in {u for u in uids if uids.count(u) > 1}:
        problems.append(f"uid {uid} is used twice")
    for _, entity in doc.entities():
        parent = entity.parent()
        if parent is not None and parent not in present:
            problems.append(f"uid {entity.uid}: parent {parent} is not in the map")
        for i, line in enumerate(entity.lines):
            m = re.match(rf"^(\s*)({LIST_KEYS}):\s*$", line)
            if m:
                j = i + 1
                while j < len(entity.lines) and re.match(rf"^{m.group(1)}- (\d+)\s*$", entity.lines[j]):
                    ref = int(entity.lines[j].strip()[2:])
                    if ref not in present:
                        problems.append(f"uid {entity.uid}: {m.group(2)} holds {ref}, which is not in the map")
                    j += 1
            m = re.match(rf"^\s*({SLOT_KEYS}): (\d+)\s*$", line)
            if m and int(m.group(2)) not in present:
                problems.append(f"uid {entity.uid}: {m.group(1)} is {m.group(2)}, which is not in the map")
    grid = map_entity = None
    for _, entity in doc.entities():
        names = entity.component_names()
        if "MapGrid" in names and grid is None:
            grid = entity
        if "Map" in names and map_entity is None:
            map_entity = entity
    if grid is not None and map_entity is not None:
        for group, entity in doc.entities():
            if entity is not grid and entity is not map_entity and entity.parent() == map_entity.uid:
                problems.append(f"uid {entity.uid} ({group.proto}) is parented to the map, not the grid")
    declared = [int(m.group(1)) for line in doc.head for m in [re.match(r"^  entityCount: (\d+)\s*$", line)] if m]
    if declared and declared[0] != len(uids):
        problems.append(f"entityCount is {declared[0]} but the map has {len(uids)} entities")
    return problems


def read_map(path: Path) -> MapDoc:
    return MapDoc(path.read_bytes().decode("utf-8-sig"))


def write_map(doc: MapDoc, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    text = doc.render()
    assert "\r\r" not in text
    path.write_bytes(text.encode("utf-8"))


def load_mapping(path: Path = MAPPING) -> dict:
    return yaml.safe_load(path.read_text(encoding="utf-8")) or {}


def load_sources() -> dict:
    return source_paths.resolve(yaml.safe_load(IMPORT_CONFIG.read_text(encoding="utf-8"))["sources"], REPO)


def output_name(path: Path, sources: dict, overrides: dict) -> str:
    """The output file name. CMU maps get a `cmu_` prefix and a lower-case name, since both repos have a van_interior.yml."""
    cmu_root = Path(overrides.get("cmu") or sources["cmu"]["path"])
    try:
        path.resolve().relative_to(cmu_root.resolve())
    except ValueError:
        return path.name
    return "cmu_" + path.name.lower()


def kept_source_maps(sources: dict, overrides: dict, rmc: bool, cmu: bool) -> list[Path]:
    paths: list[Path] = []
    if rmc:
        root = Path(overrides.get("rmc") or sources["rmc"]["path"]) / "Resources/Maps/_RMC14/Vehicles"
        paths += sorted(root.glob("*.yml"))
    if cmu:
        root = Path(overrides.get("cmu") or sources["cmu"]["path"]) / "Content.CMU/Resources/Maps/CMU14/Vehicles/Interiors"
        paths += [p for p in sorted(root.glob("*.yml")) if p.name not in SKIPPED_CMU_MAPS]
    return paths


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("maps", nargs="*", help="map files to convert")
    parser.add_argument("--rmc-all", action="store_true", help="every RMC vehicle interior map")
    parser.add_argument("--cmu-all", action="store_true", help="every CMU vehicle interior map the port keeps")
    parser.add_argument("--dry-run", action="store_true", help="convert in memory, print the report, write nothing")
    parser.add_argument("--out", help=f"output folder (default {OUT_DIR})")
    parser.add_argument("--mapping", help="mapping file (default interior_map_mapping.yml)")
    parser.add_argument("--repo", help="Wolfgate checkout to look ids up in (default: this one)")
    parser.add_argument("--rmc", help="path of the RMC-14 checkout")
    parser.add_argument("--cmu", help="path of the ColonialMarinesUniverse checkout")
    parser.add_argument("--engine", help="path of RMC-14's RobustToolbox")
    args = parser.parse_args(argv)

    mapping = load_mapping(Path(args.mapping) if args.mapping else MAPPING)
    sources = load_sources()
    overrides = {"rmc": args.rmc, "cmu": args.cmu, "engine": args.engine}
    repo = Path(args.repo) if args.repo else REPO
    paths = [Path(p) for p in args.maps] + kept_source_maps(sources, overrides, args.rmc_all, args.cmu_all)
    if not paths:
        parser.error("no maps given")
    know = Knowledge(repo, mapping, sources, overrides)
    report = Report()
    out_dir = Path(args.out) if args.out else repo / OUT_DIR
    broken = 0
    for path in paths:
        doc = read_map(path)
        name = output_name(path, sources, overrides)
        convert(doc, mapping, know, report, name)
        for problem in verify(doc):
            print(f"  BROKEN {name}: {problem}")
            broken += 1
        if not args.dry_run:
            write_map(doc, out_dir / name)
    print("\n".join(report.lines()))
    return 1 if broken else 0


if __name__ == "__main__":
    sys.exit(main())
