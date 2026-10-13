#!/usr/bin/env python3
"""Translates CMU vehicle and hardpoint prototypes onto RMC's vehicle framework (the Wolfgate port of it).

    python Tools/_WF/Vehicles/translate_cmu_yaml.py --roster --dry-run          every roster entity, report only
    python Tools/_WF/Vehicles/translate_cmu_yaml.py --roster --out SCRATCH      write the translated files under SCRATCH
    python Tools/_WF/Vehicles/translate_cmu_yaml.py FILE.yml [--ids A,B]        one CMU file (or some of its entities) to stdout

CMU wrote its hardpoints with string ids (hardpointType: Turret) and spelled out per-vehicle boilerplate; RMC uses entity
prototypes for every hardpoint, slot, family and compatibility id. The script edits the text line by line, so comments and
formatting survive, and for each entity it

  * parents it as cmu_translation.yml says,
  * prefixes hardpoint ids (Turret -> HardpointTypeTurret, Treads -> HardpointSlotTypeTreads, Tank -> HardpointVehicleFamilyTank,
    SPPTank -> HardpointCompatibilitySPPTank) and adds compatibility ids to the entities listed there,
  * drops the CMU-only components and fields (the table says why), plus any key of a checked component that RMC's C# class does
    not declare, and folds Injurable.damageContainer into Damageable,
  * points sprites, interior maps and sounds at the Wolfgate copies (the asset table of copy_assets.py), swaps CMU's unattributed
    weapon sounds for RMC's, and fixes the known data bugs (a missing flag state, repeated entry points),
  * reports everything it dropped, everything it changed, and ids and asset paths it could not resolve.

Needs PyYAML and the RMC checkout (for the component fields).
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
    sys.exit("translate_cmu_yaml.py needs PyYAML: pip install pyyaml")

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import copy_assets  # noqa: E402
import source_paths  # noqa: E402

RULES = HERE / "cmu_translation.yml"
IMPORT_CONFIG = HERE / "import_files.yml"
ITEM_START = re.compile(r"^- type: (\S+)\s*$")
COMPONENT_START = re.compile(r"^  - type: (\S+)\s*(?:#.*)?$")
FIELD_LINE = re.compile(r"^    ([A-Za-z_]\w*):(.*)$")
PLAIN_VALUE = r"[^\s#]+"


# ---------------------------------------------------------------------------------------------------------------
# Prototype text
# ---------------------------------------------------------------------------------------------------------------

class Entity:
    """The lines of one `- type: entity` item, with helpers for its component blocks."""

    def __init__(self, lines: list[str]):
        self.lines = lines

    @property
    def id(self) -> str | None:
        for line in self.lines:
            m = re.match(r"^  id: (\S+)", line)
            if m:
                return m.group(1)
        return None

    def components_start(self) -> int | None:
        for i, line in enumerate(self.lines):
            if re.match(r"^  components:\s*$", line):
                return i
        return None

    def blocks(self) -> list[tuple[str, int, int]]:
        """(component name, first line, one past the last line); trailing blank lines are not part of a block."""
        start = self.components_start()
        if start is None:
            return []
        out = []
        i = start + 1
        while i < len(self.lines):
            m = COMPONENT_START.match(self.lines[i])
            if not m:
                if self.lines[i].strip() == "" or self.lines[i].lstrip().startswith("#"):
                    i += 1
                    continue
                break
            j = i + 1
            while j < len(self.lines) and not COMPONENT_START.match(self.lines[j]) and (
                    self.lines[j].strip() == "" or self.lines[j].startswith("    ") or self.lines[j].lstrip().startswith("#")):
                j += 1
            end = j
            while end > i + 1 and self.lines[end - 1].strip() == "":
                end -= 1
            out.append((m.group(1), i, end))
            i = j
        return out

    def fields(self, start: int, end: int) -> list[tuple[str, int, int]]:
        """(field name, first line, one past the last line) of the indent-4 keys of a component block."""
        out = []
        i = start + 1
        while i < end:
            m = FIELD_LINE.match(self.lines[i])
            if not m:
                i += 1
                continue
            j = i + 1
            while j < end and (self.lines[j].startswith("     ") or self.lines[j].startswith("    - ")
                               or self.lines[j].strip() == "" or self.lines[j].lstrip().startswith("#")):
                j += 1
            while j > i + 1 and self.lines[j - 1].strip() == "":
                j -= 1
            out.append((m.group(1), i, j))
            i = j
        return out

    def block(self, name: str) -> tuple[int, int] | None:
        for n, s, e in self.blocks():
            if n == name:
                return s, e
        return None

    def delete(self, start: int, end: int) -> None:
        del self.lines[start:end]


def split_items(text: str) -> tuple[list[str], list[tuple[str, list[str]]]]:
    """(lines before the first item, [(kind, lines)]). Blank and comment lines between items stay with the item above."""
    lines = text.split("\n")
    head: list[str] = []
    items: list[tuple[str, list[str]]] = []
    for line in lines:
        m = ITEM_START.match(line)
        if m:
            items.append((m.group(1), [line]))
        elif items:
            items[-1][1].append(line)
        else:
            head.append(line)
    return head, items


# ---------------------------------------------------------------------------------------------------------------
# What RMC's C# declares
# ---------------------------------------------------------------------------------------------------------------

def camel(name: str) -> str:
    return name[:1].lower() + name[1:]


def scan_fields(text: str) -> tuple[dict[str, str], dict[str, set[str]]]:
    """({component name: class}, {class: yaml field names}) from one C# file."""
    registered: dict[str, str] = {}
    fields: dict[str, set[str]] = {}
    lines = text.split("\n")
    current = None
    for i, line in enumerate(lines):
        m = re.search(r"\b(?:class|struct) (\w+)", line)
        if m and "(" not in line.split(m.group(0))[0]:
            current = m.group(1)
            for k in range(i - 1, max(-1, i - 6), -1):
                if "RegisterComponent" in lines[k]:
                    custom = re.search(r'ComponentProtoName\("([^"]+)"\)', "\n".join(lines[max(0, k - 2):i + 1]))
                    registered[custom.group(1) if custom else current[:-9] if current.endswith("Component") else current] = current
                    break
        if "[DataField" in line and current:
            explicit = re.search(r'\[DataField(?:\(\s*"([^"]+)")?', line)
            for j in range(i, min(len(lines), i + 6)):
                decl = re.search(r"\bpublic\s+(?:readonly\s+|new\s+|required\s+)*[\w<>\[\]?,. ]+?\s+(\w+)\s*(?:[=;{]|$)", lines[j])
                if decl:
                    fields.setdefault(current, set()).add(explicit.group(1) if explicit and explicit.group(1) else camel(decl.group(1)))
                    break
    return registered, fields


class RmcFields:
    def __init__(self, root: Path, subfolders: list[str]):
        self.component_class: dict[str, str] = {}
        self.class_fields: dict[str, set[str]] = {}
        for sub in subfolders:
            for path in sorted((root / sub).rglob("*.cs")) if (root / sub).exists() else []:
                try:
                    reg, fields = scan_fields(path.read_text(encoding="utf-8-sig"))
                except (OSError, UnicodeDecodeError):
                    continue
                self.component_class.update(reg)
                for cls, names in fields.items():
                    self.class_fields.setdefault(cls, set()).update(names)

    def for_component(self, name: str) -> set[str] | None:
        cls = self.component_class.get(name)
        return self.class_fields.get(cls) if cls else None

    def for_class(self, cls: str) -> set[str] | None:
        return self.class_fields.get(cls)


# ---------------------------------------------------------------------------------------------------------------
# Translation
# ---------------------------------------------------------------------------------------------------------------

class Report:
    def __init__(self):
        self.entities = 0
        self.dropped: list[tuple[str, str, str]] = []
        self.changed: Counter = Counter()
        self.unresolved: list[tuple[str, str]] = []
        self.unknown_ids: dict[str, set[str]] = {}
        self.unknown_components: dict[str, set[str]] = {}
        self.referenced: dict[str, set[str]] = {}
        self.notes: list[str] = []
        self.passed_through: list[str] = []

    def drop(self, entity: str, what: str, why: str) -> None:
        self.dropped.append((entity, what, why))

    def lines(self) -> list[str]:
        out = [f"{self.entities} entities translated"]
        if self.dropped:
            out.append(f"dropped ({len(self.dropped)}):")
            for entity, what, why in self.dropped:
                out.append(f"  {entity}: {what}  [{why}]")
        if self.changed:
            out.append("changed: " + ", ".join(f"{k} x{v}" for k, v in sorted(self.changed.items())))
        for kind, ids in sorted(self.unknown_ids.items()):
            out.append(f"{kind} ids with no prototype (RMC's vehicle_hardpoint_types.yml and the extras define none): " + ", ".join(sorted(ids)))
        if self.unknown_components:
            out.append("components with no registration in Wolfgate, the engine or an RMC file still to be ported:")
            for name, users in sorted(self.unknown_components.items()):
                out.append(f"  {name}  in {', '.join(sorted(users))}")
        if self.referenced:
            out.append("entity ids the translated files point at that neither Wolfgate nor RMC defines (CMU-only; a roster package brings them):")
            for ident, users in sorted(self.referenced.items()):
                out.append(f"  {ident}  used by {', '.join(sorted(users))}")
        if self.unresolved:
            out.append("asset paths with no Wolfgate copy:")
            for entity, path in self.unresolved:
                out.append(f"  {entity}: {path}")
        if self.passed_through:
            out.append("items passed through as they are: " + ", ".join(self.passed_through))
        out.extend(self.notes)
        return out


class Translator:
    def __init__(self, rules: dict, rmc_fields: RmcFields | None, known_hardpoint_ids: set[str], asset_paths: dict[str, str],
                 known_components: set[str] | None = None, defined_ids: set[str] | None = None):
        self.r = rules
        self.fields = rmc_fields
        self.known_components = known_components
        self.defined_ids = defined_ids
        self.translated_ids: set[str] = set()
        self.referenced: dict[str, set[str]] = {}
        self.known = set(known_hardpoint_ids) | {p["id"] for p in rules.get("extra_prototypes") or []}
        self.assets = dict(asset_paths)
        self.report = Report()
        keys = sorted(self.assets, key=len, reverse=True)
        self._asset_re = re.compile(r"(?<![\w.-])(?:/Textures/)?(" + "|".join(re.escape(k) for k in keys) + r")(?![\w.-])") if keys else None

    # ---- entity level ----

    def translate_entity(self, entity: Entity) -> None:
        ident = entity.id or "?"
        self.report.entities += 1
        self._reparent(entity, ident)
        self._components(entity, ident)
        self._fold_injurable(entity, ident)
        self._tags(entity, ident)
        self._dedupe_entry_points(entity, ident)
        self._compatibility(entity, ident)
        self._line_rewrites(entity, ident)
        self._tidy(entity, ident)
        self.translated_ids.add(ident)
        if self.known_components is not None:
            for name, _, _ in entity.blocks():
                if name not in self.known_components:
                    self.report.unknown_components.setdefault(name, set()).add(ident)

    def _tidy(self, entity: Entity, ident: str) -> None:
        """Removes blank lines inside component blocks and collects the entity ids the translation still points at."""
        for _, start, end in reversed(entity.blocks()):
            for i in range(end - 1, start, -1):
                if entity.lines[i].strip() == "":
                    del entity.lines[i]
        if (entity.block("Fixtures") and not any(re.match(r"^\s*- Vehicle\s*$", l) for l in entity.lines)):
            self.report.notes.append(f"note: {ident} overrides Fixtures without the Vehicle layer; add it (RMC's bases carry it)")
        if self.defined_ids is not None:
            for line in entity.lines:
                m = re.match(r"^\s*(?:- )?(?:parent|startingItem|proto|bulletType|cartridgeType): ([A-Za-z]\w*)\s*(?:#.*)?$", line)
                if m and m.group(1) not in self.defined_ids:
                    self.referenced.setdefault(m.group(1), set()).add(ident)

    def _reparent(self, entity: Entity, ident: str) -> None:
        for i, line in enumerate(entity.lines):
            m = re.match(r"^  parent: (\S+)(.*)$", line)
            if m and m.group(1) in (self.r.get("parents") or {}):
                new = self.r["parents"][m.group(1)]
                entity.lines[i] = f"  parent: {new}{m.group(2)}"
                self.report.changed[f"parent {m.group(1)} -> {new}"] += 1

    def _components(self, entity: Entity, ident: str) -> None:
        drop = self.r.get("drop_components") or {}
        rename = self.r.get("rename_components") or {}
        drop_fields = self.r.get("drop_fields") or {}
        checked = set(self.r.get("check_fields") or [])
        # Work back to front so earlier line numbers stay valid.
        inherit = set(self.r.get("inherit_components") or [])
        for name, start, end in reversed(entity.blocks()):
            if name in drop and name != "Injurable":
                entity.delete(start, end)
                self.report.drop(ident, f"component {name}", drop[name])
                continue
            if name in inherit:
                values = ", ".join(self._plain(entity.lines[fs]) for _, fs, _ in entity.fields(start, end))
                entity.delete(start, end)
                self.report.drop(ident, f"component {name} ({values})", "RMC's base supplies the family values")
                continue
            for field_name, fs, fe in reversed(entity.fields(start, end)):
                why = (drop_fields.get(name) or {}).get(field_name)
                if why is None and name in checked and self.fields is not None:
                    declared = self.fields.for_component(name)
                    if declared is not None and field_name not in declared:
                        why = f"RMC's {self.fields.component_class[name]} has no such field"
                if why is not None:
                    entity.delete(fs, fe)
                    end -= fe - fs
                    self.report.drop(ident, f"field {name}.{field_name}", why)
            if name == "HardpointSlots":
                end = self._slot_entries(entity, start, end, ident)
            if name in rename:
                entity.lines[start] = re.sub(r"type: \S+", f"type: {rename[name]}", entity.lines[start], count=1)
                self.report.changed[f"component {name} -> {rename[name]}"] += 1
            if name in (self.r.get("drop_if_empty") or []) and not entity.fields(start, end) and name not in ("Injurable",):
                entity.delete(start, end)
                self.report.drop(ident, f"component {name}", "nothing left in it")

    @staticmethod
    def _plain(line: str) -> str:
        """`    key: value` as `key=value`, without a trailing comment."""
        key, _, value = line.strip().partition(":")
        return f"{key}={value.split(' #')[0].strip()}"

    def _slot_entries(self, entity: Entity, start: int, end: int, ident: str) -> int:
        drop = self.r.get("drop_slot_fields") or {}
        cls = (self.r.get("entry_classes") or {}).get("HardpointSlots.slots", "")
        declared = self.fields.for_class(cls) if self.fields and cls else None
        for i in range(end - 1, start, -1):
            m = re.match(r"^      ([A-Za-z_]\w*):", entity.lines[i])
            if not m:
                continue
            key = m.group(1)
            why = drop.get(key)
            if why is None and declared is not None and key not in declared:
                why = f"RMC's {cls} has no such field"
            if why is not None:
                j = i + 1
                while j < end and entity.lines[j].startswith("        "):
                    j += 1
                entity.delete(i, j)
                end -= j - i
                self.report.drop(ident, f"slot field {key}", why)
        return end

    def _fold_injurable(self, entity: Entity, ident: str) -> None:
        located = entity.block("Injurable")
        if not located:
            return
        start, end = located
        container = None
        for field_name, fs, fe in entity.fields(start, end):
            if field_name == "damageContainer":
                container = re.sub(r"^\s*damageContainer:\s*", "", entity.lines[fs]).strip()
        entity.delete(start, end)
        if container is None:
            self.report.drop(ident, "component Injurable", "nothing in it to fold")
            return
        container = (self.r.get("value_renames") or {}).get(container, container)
        damageable = entity.block("Damageable")
        if damageable:
            ds, de = damageable
            if not any(f == "damageContainer" for f, _, _ in entity.fields(ds, de)):
                entity.lines.insert(ds + 1, f"    damageContainer: {container}")
        else:
            at = (entity.components_start() or 0) + 1
            entity.lines[at:at] = ["  - type: Damageable", f"    damageContainer: {container}"]
        self.report.changed["Injurable.damageContainer folded into Damageable"] += 1

    def _tags(self, entity: Entity, ident: str) -> None:
        drop = self.r.get("drop_tags") or {}
        located = entity.block("Tag")
        if not located or not drop:
            return
        start, end = located
        for i in range(end - 1, start, -1):
            m = re.match(r"^\s*- (\S+)\s*$", entity.lines[i])
            if m and m.group(1) in drop:
                del entity.lines[i]
                end -= 1
                self.report.drop(ident, f"tag {m.group(1)}", drop[m.group(1)])
        remaining = [l for l in entity.lines[start + 1:end] if re.match(r"^\s*- \S", l)]
        if not remaining:
            entity.delete(start, end)
            self.report.drop(ident, "component Tag", "no tags left")

    def _dedupe_entry_points(self, entity: Entity, ident: str) -> None:
        located = entity.block("VehicleEnter")
        if not located:
            return
        start, end = located
        for field_name, fs, fe in entity.fields(start, end):
            if field_name != "entryPoints":
                continue
            items, current = [], []
            for line in entity.lines[fs + 1:fe]:
                if line.startswith("    - ") and current:
                    items.append(current)
                    current = []
                current.append(line)
            if current:
                items.append(current)
            seen, keep = set(), []
            for item in items:
                key = "\n".join(l.strip().lstrip("- ") for l in item)
                if key in seen:
                    self.report.drop(ident, "a repeated entry point", "identical to an earlier one")
                    continue
                seen.add(key)
                keep.append(item)
            if len(keep) != len(items):
                entity.lines[fs + 1:fe] = [l for item in keep for l in item]
            return

    def _compatibility(self, entity: Entity, ident: str) -> None:
        compat = (self.r.get("add_compatibility") or {}).get(ident)
        if not compat:
            return
        item = entity.block("HardpointItem")
        if item and not any(f == "compatibilityId" for f, _, _ in entity.fields(*item)):
            entity.lines.insert(item[0] + 1, f"    compatibilityId: {compat}")
            self.report.changed["compatibilityId added"] += 1
        slots = entity.block("HardpointSlots")
        if slots:
            start, end = slots
            i = start
            while i < end:
                if re.match(r"^    - id: ", entity.lines[i]):
                    j = i + 1
                    while j < end and not re.match(r"^    - id: ", entity.lines[j]):
                        j += 1
                    if not any(re.match(r"^      compatibilityId:", l) for l in entity.lines[i:j]):
                        entity.lines.insert(i + 1, f"      compatibilityId: {compat}")
                        end += 1
                        j += 1
                        self.report.changed["slot compatibilityId added"] += 1
                    i = j
                else:
                    i += 1

    # ---- line level ----

    def _line_rewrites(self, entity: Entity, ident: str) -> None:
        hp_fields = self.r.get("hardpoint_id_fields") or {}
        renames = self.r.get("value_renames") or {}
        fixes = self.r.get("fix_values") or {}
        sounds = self.r.get("sound_substitutions") or {}
        collections = self.r.get("sound_collection_substitutions") or {}
        list_drop = self.r.get("drop_list_values") or {}
        in_hardpoint = set()
        for name, start, end in entity.blocks():
            if name in ("HardpointItem", "HardpointSlots"):
                in_hardpoint.update(range(start, end))
        out: list[str] = []
        for i, line in enumerate(entity.lines):
            m = re.match(r"^(\s*)- (\S+)\s*$", line)
            if m and m.group(2) in list_drop:
                self.report.drop(ident, f"list value {m.group(2)}", list_drop[m.group(2)])
                continue
            if i in in_hardpoint:
                m = re.match(rf"^(\s*(?:- )?)({'|'.join(hp_fields)}): ({PLAIN_VALUE})(.*)$", line)
                if m and m.group(3) not in ("null", "~"):
                    prefix = hp_fields[m.group(2)]
                    value = m.group(3) if m.group(3).startswith(prefix) else prefix + m.group(3)
                    if value != m.group(3):
                        self.report.changed[f"{m.group(2)} prefixed"] += 1
                    if value not in self.known:
                        self.report.unknown_ids.setdefault(m.group(2), set()).add(value)
                    line = f"{m.group(1)}{m.group(2)}: {value}{m.group(4)}"
            for old, new in renames.items():
                if re.search(rf"(?<![\w-]){re.escape(old)}(?![\w-])", line):
                    line = re.sub(rf"(?<![\w-]){re.escape(old)}(?![\w-])", new, line)
                    self.report.changed[f"{old} -> {new}"] += 1
            for old, new in fixes.items():
                if old in line:
                    line = line.replace(old, new)
                    self.report.changed[f"fixed {old} -> {new}"] += 1
            for old, new in sounds.items():
                if old in line:
                    line = line.replace(old, new)
                    self.report.changed["sound replaced"] += 1
            for old, new in collections.items():
                m = re.match(rf"^(\s*)collection: {re.escape(old)}\s*$", line)
                if m:
                    line = f"{m.group(1)}path: {new}"
                    self.report.changed["sound collection replaced"] += 1
            line = re.sub(r"/Maps/_RMC14/Vehicles/(?!Interiors/)(\S+\.yml)", r"/Maps/_WF/Vehicles/Interiors/\1", line)
            line = re.sub(r"/Maps/CMU14/Vehicles/Interiors/(\S+?\.yml)",
                          lambda m: "/Maps/_WF/Vehicles/Interiors/cmu_" + m.group(1).lower(), line)
            if self._asset_re is not None:
                line, n = self._asset_re.subn(lambda m: self.assets[m.group(1)], line)
                if n:
                    self.report.changed["asset path rewritten"] += n
            for path in re.findall(r"(?:/Audio/|/Textures/|\b)(?:_RMC14|CMU14)/[\w./-]+", line):
                if "/Maps/" not in line:
                    self.report.unresolved.append((ident, path))
            out.append(line)
        entity.lines = out

    # ---- files ----

    def finish(self) -> Report:
        """Fills in what can only be known after every entity is done: references to ids nobody defines."""
        self.report.referenced = {k: v for k, v in self.referenced.items() if k not in self.translated_ids}
        return self.report

    def translate_text(self, text: str, ids: set[str] | None) -> str:
        text = text.lstrip("﻿")
        newline = "\r\n" if text.count("\r\n") > text.count("\n") / 2 else "\n"
        text = re.sub(r"\r+\n", "\n", text).replace("\r", "\n")
        head, items = split_items(text)
        out = list(head)
        kept = False
        for kind, lines in items:
            if kind != "entity":
                self.report.passed_through.append(f"{kind} item")
                continue
            entity = Entity(lines)
            if ids is not None and entity.id not in ids:
                continue
            self.translate_entity(entity)
            while entity.lines and entity.lines[-1].strip() == "":
                entity.lines.pop()
            out.extend(entity.lines + [""])
            kept = True
        if not kept:
            return ""
        while out and out[0].strip() == "":
            out.pop(0)
        return newline.join(out)


def load_rules(path: Path = RULES) -> dict:
    return yaml.safe_load(path.read_text(encoding="utf-8")) or {}


def load_sources() -> dict:
    return source_paths.resolve(yaml.safe_load(IMPORT_CONFIG.read_text(encoding="utf-8"))["sources"], copy_assets.REPO)


def build(rules: dict, sources: dict, overrides: dict | None = None) -> Translator:
    overrides = overrides or {}
    rmc_root = Path(overrides.get("rmc") or sources["rmc"]["path"])
    known = set()
    types_file = rmc_root / "Resources/Prototypes/_RMC14/vehicle_hardpoint_types.yml"
    if types_file.exists():
        text = types_file.read_text(encoding="utf-8-sig")
        known = set(re.findall(r"(?m)^  id: (\S+)", text))
    fields = RmcFields(rmc_root, rules.get("rmc_component_roots") or []) if rmc_root.exists() else None
    assets = copy_assets.path_map(copy_assets.load_config(), sources, overrides)
    import convert_interior_map as conv
    know = conv.Knowledge(copy_assets.REPO, conv.load_mapping(), sources, overrides)
    components = know.components | set(know.pending_components) | set(rules.get("known_components") or [])
    defined = set(know.protos) | set(know.pending_protos)
    if rmc_root.exists():
        defined |= conv.scan_prototype_ids(rmc_root / "Resources/Prototypes")["entity"]
    return Translator(rules, fields, known, assets, components, defined)


def run_roster(translator: Translator, rules: dict, sources: dict, overrides: dict, out: Path | None) -> int:
    results = 0
    for entry in rules["roster"]:
        root = Path(overrides.get("cmu") or sources["cmu"]["path"])
        path = root / entry["file"]
        if not path.exists():
            print(f"  MISSING {entry['file']}")
            results = 1
            continue
        wanted = set(entry["ids"]) if entry.get("ids") else None
        before = translator.report.entities
        text = translator.translate_text(path.read_bytes().decode("utf-8-sig"), wanted)
        got = translator.report.entities - before
        if wanted is not None and got != len(wanted):
            print(f"  MISSING ids in {entry['file']}: {sorted(wanted)} (translated {got})")
            results = 1
        print(f"  {entry['file']}: {got} entities")
        if out is not None and text:
            target = out / entry["out"]
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(text.encode("utf-8"))
    if out is not None:
        extra = "\n\n".join(p["text"].rstrip() for p in rules.get("extra_prototypes") or [])
        if extra:
            (out / "compatibility.yml").write_bytes((extra + "\n").encode("utf-8"))
    return results


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("files", nargs="*", help="CMU prototype files")
    parser.add_argument("--ids", help="comma-separated entity ids to translate (default: all in the file)")
    parser.add_argument("--roster", action="store_true", help="the CMU roster named in cmu_translation.yml")
    parser.add_argument("--dry-run", action="store_true", help="report only, write nothing")
    parser.add_argument("--out", help="folder to write the translated files into (with --roster)")
    parser.add_argument("--rules", help="rules file (default cmu_translation.yml)")
    parser.add_argument("--rmc", help="path of the RMC-14 checkout")
    parser.add_argument("--cmu", help="path of the ColonialMarinesUniverse checkout")
    parser.add_argument("--engine", help="path of RMC-14's RobustToolbox")
    args = parser.parse_args(argv)
    rules = load_rules(Path(args.rules) if args.rules else RULES)
    sources = load_sources()
    overrides = {"rmc": args.rmc, "cmu": args.cmu, "engine": args.engine}
    translator = build(rules, sources, overrides)
    status = 0
    if args.roster:
        status = run_roster(translator, rules, sources, overrides, None if args.dry_run or not args.out else Path(args.out))
    for name in args.files:
        text = translator.translate_text(Path(name).read_bytes().decode("utf-8-sig"), set(args.ids.split(",")) if args.ids else None)
        if not args.dry_run:
            sys.stdout.write(text)
    print("\n".join(translator.finish().lines()), file=sys.stderr if args.files and not args.dry_run else sys.stdout)
    return status


if __name__ == "__main__":
    sys.exit(main())
