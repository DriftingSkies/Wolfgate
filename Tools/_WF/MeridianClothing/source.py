"""Reads Meridian Rift's clothing types and draws their sprites.

An item is one /obj/item/clothing type as Wolfgate will list it: its name, slot and flags, and its sprite
sets (the item icon, the worn sprite and the in-hand sprites), each a list of layers. A layer is one piece
of art and the colour slot that tints it, or none for art that carries its own colours. Plain items have one
uncoloured layer per set; greyscale (GAGS) items have a layer per colour, flattened out of their configs.
"""
import json
import os
import re
import sys

from PIL import Image, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
# The markings port's DM and DMI readers; appended so its dedupe.py doesn't shadow this one.
sys.path.append(os.path.join(HERE, "..", "MeridianMarkings"))

import ss13  # noqa: E402

TILE = 32

# Second typepath segment -> Wolfgate slot.
SLOTS = {
    "under": "jumpsuit",
    "suit": "outerClothing",
    "head": "head",
    "mask": "mask",
    "shoes": "shoes",
    "gloves": "gloves",
    "glasses": "eyes",
    "neck": "neck",
    "accessory": "neck",
    "ears": "ears",
}

# Wolfgate slot -> the equipped state name its clothing draws.
EQUIPPED = {
    "jumpsuit": "equipped-INNERCLOTHING",
    "outerClothing": "equipped-OUTERCLOTHING",
    "head": "equipped-HELMET",
    "mask": "equipped-MASK",
    "shoes": "equipped-FEET",
    "gloves": "equipped-HAND",
    "eyes": "equipped-EYES",
    "neck": "equipped-NECK",
    "ears": "equipped-EARS",
}

# SS13 worn sheet a slot falls back to when the type names none.
DEFAULT_WORN = {
    "jumpsuit": "icons/mob/clothing/under/default.dmi",
    "outerClothing": "icons/mob/clothing/suits/default.dmi",
    "head": "icons/mob/clothing/head/default.dmi",
    "mask": "icons/mob/clothing/mask.dmi",
    "shoes": "icons/mob/clothing/feet.dmi",
    "gloves": "icons/mob/clothing/hands.dmi",
    "eyes": "icons/mob/clothing/eyes.dmi",
    "neck": "icons/mob/clothing/neck.dmi",
    "ears": "icons/mob/clothing/ears.dmi",
}

FLAGS_INV = {
    "HIDEGLOVES": 1 << 0, "HIDESUITSTORAGE": 1 << 1, "HIDEJUMPSUIT": 1 << 2, "HIDESHOES": 1 << 3,
    "HIDEMASK": 1 << 4, "HIDEEARS": 1 << 5, "HIDEEYES": 1 << 6, "HIDEFACE": 1 << 7, "HIDEHAIR": 1 << 8,
    "HIDEFACIALHAIR": 1 << 9, "HIDENECK": 1 << 10, "HIDEHEADGEAR": 1 << 11, "HIDESNOUT": 1 << 12,
    "HIDEMUTWINGS": 1 << 13, "HIDEBELT": 1 << 14, "HIDEANTENNAE": 1 << 15, "HIDETAIL": 1 << 16,
}


_IMPROPER = re.compile(r"\\(improper|proper|the|a|an|The|A|An|roman|Roman|ordinal|icon|th|s|he|she|his|him|himself|herself|hers|its)\b ?")


class Layer:
    """One piece of art: frames[frame][dir] -> RGBA image, and the colour slot (1-based) or None."""

    def __init__(self, frames, color, delays=None):
        self.frames = frames
        self.color = color
        self.delays = delays

    @property
    def dirs(self):
        return len(self.frames[0])


class Item:
    def __init__(self, typepath):
        self.typepath = typepath
        self.name = None
        self.desc = None
        self.slot = None
        self.icon = []          # layers of the item icon
        self.worn = []          # layers of the worn sprite
        self.inhand_left = []
        self.inhand_right = []
        self.colors = []        # default colour per slot, "#RRGGBB"
        self.flags_inv = 0
        self.covers = set()     # body parts covered
        self.armor = None       # SS13 armor datum path
        self.source = None      # the DMI or config the item icon came from, relative to the SS13 root
        self.player_colorable = False

    @property
    def gags(self):
        return bool(self.colors)


def clean_text(text):
    if not isinstance(text, str):
        return None
    text = _IMPROPER.sub("", text)
    text = re.sub(r"\[[^\]]*\]", "", text)
    text = text.replace('\\"', '"').replace("\\n", " ").replace("\\'", "'")
    return re.sub(r"\s+", " ", text).strip() or None


def file_literal(value):
    """'icons/x.dmi' as the evaluator returns it (a string) or None."""
    if isinstance(value, str) and value.endswith((".dmi", ".json")):
        return value.strip("'\"")
    return None


def flag_value(datums, raw, table):
    """A |-joined flag expression as an int, by the names in table; unknown names count as zero."""
    if raw is None:
        return 0
    value = 0
    for name in re.findall(r"[A-Z_][A-Z0-9_]*", raw):
        value |= table.get(name, 0)
    return value


def parse_colors(text):
    if not isinstance(text, str):
        return []
    return ["#" + c.upper() if len(c) == 6 else "#" + "".join(ch * 2 for ch in c).upper()
            for c in re.findall(r"#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})(?![0-9a-fA-F])", text)]


def hex_rgb(color):
    return tuple(int(color[i:i + 2], 16) for i in (1, 3, 5))


def tint(image, color):
    """Multiplies the RGB of an image by a colour, the way BYOND's ICON_MULTIPLY and a layer colour do."""
    if color is None:
        return image
    r, g, b = hex_rgb(color)
    bands = image.split()
    out = [bands[0].point(lambda v: v * r // 255), bands[1].point(lambda v: v * g // 255),
           bands[2].point(lambda v: v * b // 255), bands[3]]
    return Image.merge("RGBA", out)


def blank(size):
    return Image.new("RGBA", size, (0, 0, 0, 0))


def state_frames(dmi, name):
    """A DMI state's frames[frame][dir] and delays, or None."""
    state = dmi.states.get(name)
    if state is None:
        return None
    delays = [d / 10 for d in state.delays] if state.delays and state.frames > 1 else None
    return [list(row) for row in state.images], delays


def fit(frames, dirs, count):
    """Frames broadcast to the given number of dirs and frames."""
    out = []
    for f in range(count):
        row = frames[min(f, len(frames) - 1)]
        if len(row) < dirs:
            row = [row[min(d, len(row) - 1)] if len(row) > 1 else row[0] for d in range(dirs)]
        out.append(row[:dirs])
    return out


def composite(layers, colors):
    """All layers drawn into one, tinted with the given colours."""
    dirs = max(l.dirs for l in layers)
    count = max(len(l.frames) for l in layers)
    size = layers[0].frames[0][0].size
    out = [[blank(size) for _ in range(dirs)] for _ in range(count)]
    for layer in layers:
        color = colors[layer.color - 1] if layer.color and layer.color <= len(colors) else None
        frames = fit(layer.frames, dirs, count)
        for f in range(count):
            for d in range(dirs):
                out[f][d].alpha_composite(tint(frames[f][d], color))
    delays = next((l.delays for l in layers if l.delays and len(l.delays) == count), None)
    return Layer(out, None, delays)


def is_blank(layer):
    return all(img.getchannel("A").getbbox() is None for row in layer.frames for img in row)


class Gags:
    """Greyscale configs: renders one state of a config into colour-slotted layers."""

    def __init__(self, source):
        self.source = source
        self.json_cache = {}
        self.baked = 0

    def config(self, path):
        datums = self.source.datums
        icon = file_literal(datums.value(path, "icon_file"))
        js = file_literal(datums.value(path, "json_config"))
        if icon is None or js is None:
            return None
        return icon, js

    def load_json(self, rel):
        if rel not in self.json_cache:
            with open(os.path.join(self.source.root, rel), encoding="utf-8") as f:
                self.json_cache[rel] = json.load(f)
        return self.json_cache[rel]

    def render(self, config_path, state, colors, depth=0):
        """
        The layers of a config's state, each tinted by the colour slot it takes, or None. A group or
        blend this engine can't layer (multiply, add, ...) is baked with the default colours.
        """
        cfg = self.config(config_path)
        if cfg is None or depth > 6:
            return None
        icon_rel, json_rel = cfg
        try:
            data = self.load_json(json_rel)
            dmi = self.source.dmi(icon_rel)
        except (OSError, ValueError):
            return None
        if state not in data:
            return None
        return self._layers(data[state], dmi, colors, list(range(1, len(colors) + 1)), depth)

    def _layers(self, entries, dmi, colors, slot_map, depth):
        out = []
        for entry in entries:
            if isinstance(entry, list):
                group = self._layers(entry, dmi, colors, slot_map, depth)
                if group is None:
                    continue
                out.extend(group)
                continue
            mode = entry.get("blend_mode", "overlay")
            ids = entry.get("color_ids") or []
            slot = None
            if ids and isinstance(ids[0], int):
                slot = slot_map[ids[0] - 1] if ids[0] - 1 < len(slot_map) else None
            elif ids and isinstance(ids[0], str) and ids[0].startswith("#"):
                slot = ids[0].upper()  # a fixed colour
            kind = entry.get("type")
            if kind == "icon_state":
                found = state_frames(dmi, entry.get("icon_state"))
                if found is None:
                    continue
                layers = [Layer(found[0], slot, found[1])]
            elif kind == "reference":
                ref_map = [slot_map[i - 1] if isinstance(i, int) and i - 1 < len(slot_map) else None for i in ids]
                inner = self.render_ref(entry.get("reference_type"), entry.get("icon_state"), colors, ref_map, depth)
                if inner is None:
                    continue
                layers = inner
            else:
                continue
            layers = [self._fixed(l) for l in layers]
            if mode == "overlay":
                out.extend(layers)
            elif mode == "multiply" and out:
                # Shading over what is below: bake everything so far with it.
                base = composite(out, colors)
                shade = composite(layers, colors)
                out = [self._multiply(base, shade)]
                self.baked += 1
            elif mode == "underlay":
                out = layers + out
            else:
                self.baked += 1
                out.extend(layers)
        return out

    def render_ref(self, path, state, colors, slot_map, depth):
        cfg = self.config(path)
        if cfg is None:
            return None
        icon_rel, json_rel = cfg
        try:
            data = self.load_json(json_rel)
            dmi = self.source.dmi(icon_rel)
        except (OSError, ValueError):
            return None
        if state is None:
            state = next(iter(data), None)
        if state not in data:
            return None
        return self._layers(data[state], dmi, colors, slot_map, depth + 1)

    @staticmethod
    def _fixed(layer):
        """A layer tinted by a fixed colour is baked so it needs no slot."""
        if isinstance(layer.color, str):
            frames = [[tint(img, layer.color) for img in row] for row in layer.frames]
            return Layer(frames, None, layer.delays)
        return layer

    @staticmethod
    def _multiply(base, shade):
        dirs = max(base.dirs, shade.dirs)
        count = max(len(base.frames), len(shade.frames))
        b = fit(base.frames, dirs, count)
        s = fit(shade.frames, dirs, count)
        out = []
        for f in range(count):
            row = []
            for d in range(dirs):
                img = b[f][d]
                sh = s[f][d]
                # Transparent shade pixels leave the base as it is.
                white = Image.new("RGBA", img.size, (255, 255, 255, 255))
                white.alpha_composite(sh)
                rgb = ImageChops.multiply(img.convert("RGB"), white.convert("RGB"))
                merged = rgb.convert("RGBA")
                merged.putalpha(img.getchannel("A"))
                row.append(merged)
            out.append(row)
        return Layer(out, None, base.delays)


def merge_runs(layers):
    """Adjacent layers with the same colour slot drawn into one; blank layers dropped."""
    out = []
    for layer in layers:
        if is_blank(layer):
            continue
        if out and out[-1].color == layer.color:
            prev = out[-1]
            merged = composite([prev, layer], [])
            merged.color = layer.color
            out[-1] = merged
        else:
            out.append(layer)
    return out


class Source:
    def __init__(self, root):
        self.root = root
        self.datums = ss13.load_datums(
            root, ("/obj/item/clothing", "/datum/greyscale_config", "/datum/armor", "/obj/item\n"))
        self.gags = Gags(self)
        self.files = self._files()

    def _files(self):
        """Typepath -> the file that first defines it, relative to the root."""
        files = {}
        pattern = re.compile(r"^(/obj/item/clothing/[\w/]+)\s*$", re.M)
        for rel in ss13.dme_files(self.root):
            try:
                with open(os.path.join(self.root, rel), encoding="utf-8", errors="replace") as f:
                    text = f.read()
            except OSError:
                continue
            for m in pattern.finditer(text):
                files.setdefault(m.group(1), rel)
        return files

    def dmi(self, rel):
        return ss13.Dmi.load(os.path.join(self.root, rel))

    def value(self, t, var, default=None):
        return self.datums.value(t, var, default)

    def types(self):
        return [t for t in self.datums.order if t.startswith("/obj/item/clothing/")]

    def is_abstract(self, t):
        raw = self.datums.types.get(t, {}).get("abstract_type")
        return raw is not None and raw.strip() == t

    def slot_of(self, t):
        parts = t.split("/")
        return SLOTS.get(parts[4]) if len(parts) > 4 else None

    def plain_set(self, icon_rel, state):
        """One uncoloured layer from a DMI state, or []."""
        if not icon_rel or not state:
            return []
        try:
            dmi = self.dmi(icon_rel)
        except (OSError, ValueError):
            return []
        found = state_frames(dmi, state)
        if found is None:
            return []
        return [Layer(found[0], None, found[1])]

    def gags_set(self, config, state, colors):
        if not config or not state:
            return []
        layers = self.gags.render(config, state, colors)
        return merge_runs(layers) if layers else []

    def read(self, t):
        item = Item(t)
        v = lambda var, default=None: self.value(t, var, default)
        item.slot = self.slot_of(t)
        item.name = clean_text(v("name"))
        item.desc = clean_text(v("desc"))
        state = v("post_init_icon_state") or v("icon_state")
        if not isinstance(state, str):
            state = None
        worn_state = v("worn_icon_state")
        worn_state = worn_state if isinstance(worn_state, str) else state
        inhand_state = v("inhand_icon_state")
        inhand_state = inhand_state if isinstance(inhand_state, str) else state
        item.flags_inv = flag_value(self.datums, self.datums.raw(t, "flags_inv"), FLAGS_INV)
        item.covers = set(re.findall(r"[A-Z_]+", self.datums.raw(t, "body_parts_covered") or ""))
        item.armor = v("armor_type") if isinstance(v("armor_type"), str) else None
        flags1 = self.datums.raw(t, "flags_1") or ""
        item.player_colorable = "IS_PLAYER_COLORABLE_1" in flags1

        config = v("greyscale_config")
        colors = parse_colors(v("greyscale_colors"))
        config = config if isinstance(config, str) and config.startswith("/datum/greyscale_config") else None
        if config and colors:
            item.colors = colors
            item.icon = self.gags_set(config, state, colors)
            item.source = config
            worn_cfg = v("greyscale_config_worn")
            if isinstance(worn_cfg, str) and worn_cfg.startswith("/datum"):
                item.worn = self.gags_set(worn_cfg, worn_state, colors)
            for side in ("left", "right"):
                cfg = v("greyscale_config_inhand_" + side)
                if isinstance(cfg, str) and cfg.startswith("/datum"):
                    setattr(item, "inhand_" + side, self.gags_set(cfg, inhand_state, colors))
        else:
            icon = file_literal(v("icon"))
            item.icon = self.plain_set(icon, state)
            item.source = icon
        if not item.worn and item.slot:
            worn = file_literal(v("worn_icon")) or DEFAULT_WORN.get(item.slot)
            item.worn = self.plain_set(worn, worn_state)
        for side in ("left", "right"):
            if not getattr(item, "inhand_" + side):
                sheet = file_literal(v(side + "hand_file"))
                setattr(item, "inhand_" + side, self.plain_set(sheet, inhand_state))
        # A colour slot no layer uses is dropped, and the rest renumbered.
        if item.colors:
            used = sorted({l.color for s in (item.icon, item.worn, item.inhand_left, item.inhand_right)
                           for l in s if l.color})
            if not used:
                item.colors = []
            else:
                remap = {old: i + 1 for i, old in enumerate(used)}
                item.colors = [item.colors[old - 1] for old in used]
                for s in (item.icon, item.worn, item.inhand_left, item.inhand_right):
                    for l in s:
                        if l.color:
                            l.color = remap[l.color]
        return item
