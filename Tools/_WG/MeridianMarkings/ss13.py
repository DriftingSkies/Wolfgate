"""Reads what the port needs out of an SS13 (BYOND) checkout: datum definitions and DMI icon sheets.

The DM reader is not a compiler. It understands the flat style the sprite accessory and body marking
files are written in - a typepath at column zero followed by tab-indented `var = value` lines - and
resolves a variable by walking up the typepath, the way inheritance does for compile-time values.
Proc bodies are skipped. Files are read in the order tgstation.dme includes them, so a later file
overrides an earlier one as it does in the game.
"""
import os
import re

from PIL import Image

# ------------------------------------------------------------------------------------------- DM


def _strip_comments(text):
    """Removes // and /* */ comments, leaving strings and file literals alone."""
    out = []
    i, n = 0, len(text)
    quote = None
    while i < n:
        c = text[i]
        if quote:
            out.append(c)
            if c == "\\" and i + 1 < n:
                out.append(text[i + 1])
                i += 2
                continue
            if c == quote or c == "\n":
                quote = None
            i += 1
            continue
        if c in "\"'":
            quote = c
            out.append(c)
            i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            end = text.find("*/", i + 2)
            end = n if end < 0 else end + 2
            out.append("\n" * text.count("\n", i, end))
            i = end
            continue
        out.append(c)
        i += 1
    return "".join(out)


_VAR = re.compile(r"^(?:var/(?:[\w/]+/)?)?(\w+)\s*=\s*(.*)$")
_DEFINE = re.compile(r"^#define\s+(\w+)\s+(.+?)\s*$")


def _depth(text):
    depth = 0
    quote = None
    for i, c in enumerate(text):
        if quote:
            if c == quote and text[i - 1] != "\\":
                quote = None
        elif c in "\"'":
            quote = c
        elif c in "([":
            depth += 1
        elif c in ")]":
            depth -= 1
    return depth


class Datums:
    """Typepath -> {var: raw value text}, with inherited lookup."""

    def __init__(self):
        self.types = {}
        self.defines = {}
        self.order = []

    def read_defines(self, path):
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in _strip_comments(f.read()).splitlines():
                m = _DEFINE.match(line)
                if m and "(" not in m.group(1):
                    self.defines.setdefault(m.group(1), m.group(2).strip())

    def read_file(self, path):
        with open(path, encoding="utf-8", errors="replace") as f:
            lines = _strip_comments(f.read()).splitlines()
        current = None
        i = 0
        while i < len(lines):
            line = lines[i].rstrip()
            i += 1
            if not line.strip():
                continue
            if line[0] == "#":
                m = _DEFINE.match(line)
                if m and "(" not in m.group(1):
                    self.defines.setdefault(m.group(1), m.group(2).strip())
                continue
            if not line[0].isspace():
                # A typepath on its own opens a definition; anything with a call or a proc is skipped.
                if line.startswith("/") and "(" not in line and "/proc/" not in line and "/verb/" not in line \
                        and "=" not in line:
                    current = line.strip()
                    if current not in self.types:
                        self.types[current] = {}
                        self.order.append(current)
                else:
                    current = None
                continue
            if current is None:
                continue
            indent = len(line) - len(line.lstrip("\t "))
            if indent != 1 and not (line.startswith("    ") and indent == 4):
                continue
            m = _VAR.match(line.strip())
            if not m:
                continue
            name, value = m.group(1), m.group(2)
            depth = _depth(value)
            while depth > 0 and i < len(lines):
                value += " " + lines[i].strip()
                depth = _depth(value)
                i += 1
            self.types[current][name] = value.strip()

    def raw(self, path, var, default=None):
        """The raw text of a variable on a type, inherited from the nearest ancestor that sets it."""
        while path:
            mine = self.types.get(path)
            if mine is not None and var in mine:
                return mine[var]
            path = path.rsplit("/", 1)[0]
        return default

    def value(self, path, var, default=None):
        """A variable as a Python value: string, number, bool, None or the raw text for anything else."""
        return self.evaluate(self.raw(path, var), default)

    def evaluate(self, raw, default=None):
        if raw is None:
            return default
        raw = raw.strip()
        seen = 0
        while raw in self.defines and seen < 8:
            raw = self.defines[raw].strip()
            seen += 1
        if len(raw) >= 2 and raw[0] == raw[-1] and raw[0] in "\"'":
            return raw[1:-1]
        if raw == "TRUE":
            return True
        if raw == "FALSE":
            return False
        if raw == "null":
            return None
        if raw.startswith("(") and raw.endswith(")"):
            return self.evaluate(raw[1:-1], default)
        try:
            return int(raw)
        except ValueError:
            pass
        try:
            return float(raw)
        except ValueError:
            return raw

    def subtypes(self, base):
        prefix = base + "/"
        return [t for t in self.order if t.startswith(prefix)]


def dme_files(root, dme="tgstation.dme"):
    """Every .dm file the environment includes, in include order."""
    files = []
    with open(os.path.join(root, dme), encoding="utf-8") as f:
        for line in f:
            m = re.match(r'#include\s+"(.+\.dm)"', line.strip())
            if m:
                files.append(m.group(1).replace("\\", "/"))
    return files


def load_datums(root, wanted=("/datum/sprite_accessory", "/datum/body_marking")):
    """Defines from code/__DEFINES and every included file that defines one of the wanted types."""
    datums = Datums()
    files = dme_files(root)
    for rel in files:
        if rel.startswith("code/__DEFINES/"):
            datums.read_defines(os.path.join(root, rel))
    pattern = re.compile(r"^(?:%s)" % "|".join(re.escape(w) for w in wanted), re.M)
    for rel in files:
        if rel.startswith("code/__DEFINES/"):
            continue
        path = os.path.join(root, rel)
        try:
            with open(path, encoding="utf-8", errors="replace") as f:
                text = f.read()
        except OSError:
            continue
        if pattern.search(text):
            datums.read_file(path)
    return datums


# ------------------------------------------------------------------------------------------ DMI


class DmiState:
    def __init__(self, name, dirs, frames, delays, movement):
        self.name = name
        self.dirs = dirs
        self.frames = frames
        self.delays = delays  # deciseconds per frame, or None
        self.movement = movement
        self.images = []  # [frame][dir] -> RGBA Image


class Dmi:
    """
    One icon sheet. A movement state never shadows a plain one of the same name. Within a state the
    cells run frame by frame, each frame's facings south, north, east, west - the RSI order for four.
    """

    _cache = {}

    def __init__(self, path):
        image = Image.open(path)
        description = image.info.get("Description") or getattr(image, "text", {}).get("Description")
        if description is None:
            raise ValueError("%s has no DMI metadata" % path)
        self.path = path
        self.width = self.height = 32
        self.states = {}
        self.all_states = []
        current = None
        for line in description.splitlines():
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            key, _, value = line.partition("=")
            key, value = key.strip(), value.strip()
            if key == "state":
                current = DmiState(value[1:-1].replace('\\"', '"'), 1, 1, None, False)
                self.all_states.append(current)
            elif current is None:
                if key == "width":
                    self.width = int(value)
                elif key == "height":
                    self.height = int(value)
            elif key == "dirs":
                current.dirs = int(value)
            elif key == "frames":
                current.frames = int(value)
            elif key == "delay":
                current.delays = [float(v) for v in value.split(",")]
            elif key == "movement":
                current.movement = value != "0"
        image = image.convert("RGBA")
        columns = image.width // self.width
        index = 0
        for state in self.all_states:
            for _frame in range(state.frames):
                row = []
                for _dir in range(state.dirs):
                    x = (index % columns) * self.width
                    y = (index // columns) * self.height
                    row.append(image.crop((x, y, x + self.width, y + self.height)))
                    index += 1
                state.images.append(row)
            if state.name not in self.states or (self.states[state.name].movement and not state.movement):
                self.states[state.name] = state

    @classmethod
    def load(cls, path):
        path = os.path.normpath(path)
        if path not in cls._cache:
            cls._cache[path] = cls(path)
        return cls._cache[path]
