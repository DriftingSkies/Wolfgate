"""Reads the loadouts and vending machines already in this repository, and writes the port's marked blocks into them.

The port adds its loadouts to existing loadout groups and its items to existing vending inventories. Those
files are upstream, so each addition is one WOLFGATE(MeridianClothing) block at the end of the list or map,
which the port replaces wholesale on every run.
"""
import glob
import os
import re

import yaml

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
PROTOTYPES = os.path.join(ROOT, "Resources", "Prototypes")
TEXTURES = os.path.join(ROOT, "Resources", "Textures")

MODULE = "MeridianClothing"
BLOCK_START = "# WOLFGATE(%s) START: Meridian Rift clothing, written by Tools/_WF/%s/port.py" % (MODULE, MODULE)
BLOCK_END = "# WOLFGATE END"


class LooseLoader(yaml.SafeLoader):
    """SafeLoader that reads the engine's !type: tags as plain data."""


def _construct_unknown(loader, suffix, node):
    if isinstance(node, yaml.MappingNode):
        return loader.construct_mapping(node, deep=True)
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node, deep=True)
    return loader.construct_scalar(node)


LooseLoader.add_multi_constructor("", _construct_unknown)
LooseLoader.add_multi_constructor("!", _construct_unknown)


class Repo:
    def __init__(self):
        self.loadouts = {}      # id -> {slot: entity}
        self.groups = {}        # id -> (file, [loadout ids])
        self.roles = {}         # id -> [group ids]
        self.gear = {}          # startingGear id -> {slot: entity}
        self.inventories = {}   # vending inventory id -> file
        self.entities = {}      # id -> raw prototype
        for path in glob.glob(os.path.join(PROTOTYPES, "**", "*.yml"), recursive=True):
            if os.sep + "_WF" + os.sep + MODULE + os.sep in path:
                continue
            try:
                with open(path, encoding="utf-8-sig") as f:
                    docs = yaml.load(f, Loader=LooseLoader)
            except (yaml.YAMLError, UnicodeDecodeError):
                continue
            if not isinstance(docs, list):
                continue
            for doc in docs:
                if not isinstance(doc, dict) or "id" not in doc:
                    continue
                kind = doc.get("type")
                pid = str(doc["id"])
                if kind == "loadout":
                    self.loadouts[pid] = (doc.get("equipment") or {}, doc.get("startingGear"))
                elif kind == "loadoutGroup":
                    self.groups[pid] = (path, [str(x) for x in doc.get("loadouts") or []])
                elif kind == "roleLoadout":
                    self.roles[pid] = [str(x) for x in doc.get("groups") or []]
                elif kind == "startingGear":
                    self.gear[pid] = doc.get("equipment") or {}
                elif kind == "vendingMachineInventory":
                    self.inventories[pid] = path
                elif kind == "entity":
                    self.entities[pid] = doc

    def loadout_slots(self, loadout_id):
        found = self.loadouts.get(loadout_id)
        if found is None:
            return {}
        equipment, gear = found
        slots = dict(equipment)
        if gear and gear in self.gear:
            slots.update(self.gear[gear])
        return slots

    def group_slot(self, group_id):
        """The equip slot most of a group's loadouts fill, or None."""
        counts = {}
        for loadout in self.groups.get(group_id, (None, []))[1]:
            for slot in self.loadout_slots(loadout):
                counts[slot] = counts.get(slot, 0) + 1
        if not counts:
            return None
        return max(sorted(counts), key=lambda s: counts[s])

    def entity_names(self):
        """Lower-cased names of every entity here, from its prototype or its Fluent name."""
        names = set()
        for doc in self.entities.values():
            name = doc.get("name")
            if isinstance(name, str):
                names.add(name.strip().lower())
        locale = os.path.join(ROOT, "Resources", "Locale", "en-US")
        for path in glob.glob(os.path.join(locale, "**", "*.ftl"), recursive=True):
            if os.sep + "_WF" + os.sep + MODULE + os.sep in path:
                continue
            with open(path, encoding="utf-8-sig", errors="replace") as f:
                for line in f:
                    m = re.match(r"^ent-(\S+)\s*=\s*(.+?)\s*$", line)
                    if m and m.group(1) in self.entities:
                        names.add(m.group(2).lower())
        return names


def _newline(text):
    return "\r\n" if "\r\n" in text else "\n"


def _strip_block(lines):
    out = []
    skipping = False
    for line in lines:
        stripped = line.strip()
        if stripped.startswith("# WOLFGATE(%s) START" % MODULE):
            skipping = True
            continue
        if skipping:
            if stripped == BLOCK_END:
                skipping = False
            continue
        out.append(line)
    return out


def _prototype_span(lines, kind, pid):
    """Line range [start, end) of a prototype in a file's lines."""
    start = None
    for i, line in enumerate(lines):
        if line.startswith("- type:"):
            if start is not None:
                return start, i
            if line.split(":", 1)[1].split("#", 1)[0].strip() == kind:
                # The id is on one of the next lines of the same prototype.
                for j in range(i + 1, len(lines)):
                    if lines[j].startswith("- "):
                        break
                    if re.match(r"^  id:\s*%s\s*(#.*)?$" % re.escape(pid), lines[j]):
                        start = i
                        break
    if start is not None:
        return start, len(lines)
    return None


def _insert_block(path, kind, pid, key, entries):
    """
    Replaces the port's block inside one prototype's list or map field (key: "loadouts" or
    "startingInventory") with the given entry lines, or removes it when there are none.
    """
    with open(path, encoding="utf-8", newline="") as f:
        text = f.read()
    nl = _newline(text)
    bom = text.startswith("﻿")
    body = text[1:] if bom else text
    trailing = body.endswith(nl)
    lines = body.split(nl)
    if trailing:
        lines = lines[:-1]
    span = _prototype_span(lines, kind, pid)
    if span is None:
        raise SystemExit("%s: no %s %s" % (path, kind, pid))
    start, end = span
    proto = _strip_block(lines[start:end])
    # The field's own line, then its items: the indented lines after it.
    field = next((i for i, l in enumerate(proto) if re.match(r"^  %s:" % key, l)), None)
    if field is None:
        raise SystemExit("%s: %s %s has no %s" % (path, kind, pid, key))
    last = field
    indent = None
    for i in range(field + 1, len(proto)):
        line = proto[i]
        if not line.strip():
            continue
        if re.match(r"^  [^ #-]", line) or line.startswith("- "):
            break
        if re.match(r"^  ([ -]|#)", line):
            if indent is None and not line.strip().startswith("#"):
                indent = re.match(r"^(\s*)", line).group(1)
            last = i
            continue
        break
    if indent is None:
        indent = "  " if key == "loadouts" else "    "
    block = []
    if entries:
        block = [indent + BLOCK_START] + [indent + e for e in entries] + [indent + BLOCK_END]
    proto = proto[:last + 1] + block + proto[last + 1:]
    lines = lines[:start] + proto + lines[end:]
    out = nl.join(lines) + (nl if trailing else "")
    if bom:
        out = "﻿" + out
    if out != text:
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(out)
        return True
    return False


def write_group(path, group_id, loadout_ids):
    return _insert_block(path, "loadoutGroup", group_id, "loadouts", ["- %s" % l for l in loadout_ids])


def write_inventory(path, inventory_id, items):
    return _insert_block(path, "vendingMachineInventory", inventory_id, "startingInventory",
                         ["%s: %d" % (e, n) for e, n in items])


def blocks_in(paths):
    """Files under Resources/Prototypes outside the module that hold one of the port's blocks."""
    found = []
    for path in glob.glob(os.path.join(PROTOTYPES, "**", "*.yml"), recursive=True):
        if os.sep + "_WF" + os.sep + MODULE + os.sep in path:
            continue
        with open(path, encoding="utf-8", errors="replace") as f:
            if "# WOLFGATE(%s) START" % MODULE in f.read():
                found.append(path)
    return found
