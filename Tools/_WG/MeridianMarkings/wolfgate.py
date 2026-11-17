"""Reads what the port needs out of this repository: the markings that already exist and the species' bodies.

Art is compared as silhouettes. Every frame is laid on one fixed canvas centred on the mob and turned
into a bitmask held in a Python int, so an overlap is a bitwise AND and a pixel count is a popcount.
"""
import json
import os
import re

import yaml
from PIL import Image, ImageChops

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
PROTOTYPES = os.path.join(ROOT, "Resources", "Prototypes")
TEXTURES = os.path.join(ROOT, "Resources", "Textures")

# Everything is compared on this canvas; the largest marking sheet in the repository is 96 wide.
CANVAS = (128, 128)
ALPHA_MIN = 24

BODY_LAYERS = ("Chest", "Head", "LArm", "RArm", "LHand", "RHand", "LLeg", "RLeg", "LFoot", "RFoot")


class LooseLoader(yaml.SafeLoader):
    """SafeLoader that reads the engine's !type: tags as plain data."""


def _construct_unknown(loader, suffix, node):
    if isinstance(node, yaml.MappingNode):
        data = loader.construct_mapping(node, deep=True)
        data["!type"] = suffix
        return data
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node, deep=True)
    return loader.construct_scalar(node)


LooseLoader.add_multi_constructor("", _construct_unknown)
LooseLoader.add_multi_constructor("!", _construct_unknown)


# What hides art drawn below the body: the alpha of a standard body, one image per facing. Forks trim
# the hidden pixels off their tails as often as not, so art is compared as it shows on a mob.
_occluder = None


def set_occluder(images):
    global _occluder
    _occluder = [image.getchannel("A") for image in images]


def on_canvas(image, behind_direction=None):
    """An image centred on the comparison canvas; with a facing, minus what the body would hide."""
    canvas = Image.new("RGBA", CANVAS, (0, 0, 0, 0))
    canvas.paste(image.convert("RGBA"), ((CANVAS[0] - image.width) // 2, (CANVAS[1] - image.height) // 2))
    if behind_direction is not None and _occluder is not None:
        canvas.putalpha(ImageChops.multiply(canvas.getchannel("A"), ImageChops.invert(_occluder[behind_direction])))
    return canvas


def mask_of(image, behind_direction=None):
    """Bitmask of the opaque pixels of an image, centred on the comparison canvas."""
    alpha = image.getchannel("A") if image.mode == "RGBA" else image.convert("RGBA").getchannel("A")
    if alpha.getbbox() is None:
        return 0
    canvas = Image.new("L", CANVAS, 0)
    canvas.paste(alpha, ((CANVAS[0] - image.width) // 2, (CANVAS[1] - image.height) // 2))
    if behind_direction is not None and _occluder is not None:
        canvas = ImageChops.multiply(canvas, ImageChops.invert(_occluder[behind_direction]))
    bits = canvas.point(lambda a: 255 if a >= ALPHA_MIN else 0).convert("1")
    return int.from_bytes(bits.tobytes(), "big")


def overlap(a, b):
    """
    Intersection over union of two silhouettes. A silhouette is eight masks: the four facings in
    full, then the four facings as they show on a mob. Each facing is scored whichever way agrees
    better, since one fork trims what the body hides and the next draws the same art in front of it.
    """
    inter = union = 0
    for d in range(4):
        full = ((a[d] & b[d]).bit_count(), (a[d] | b[d]).bit_count())
        seen = ((a[d + 4] & b[d + 4]).bit_count(), (a[d + 4] | b[d + 4]).bit_count())
        # Cross-multiplied comparison of the two ratios; an empty facing agrees with an empty facing.
        best = full if full[0] * max(seen[1], 1) >= seen[0] * max(full[1], 1) and full[1] else seen
        inter += best[0]
        union += best[1]
    return inter / union if union else 0.0


def area(a):
    """Pixels of a silhouette in full, over the four facings."""
    return sum(a[d].bit_count() for d in range(4))


def union_of(masks):
    out = [0] * 8
    for mask in masks:
        for i in range(8):
            out[i] |= mask[i]
    return out


def silhouette(frames, behind=False):
    """Eight masks for four facings of one sprite: in full, then as they show on a mob."""
    full = [mask_of(frame) for frame in frames]
    if not behind:
        return full + full
    return full + [mask_of(frame, d) for d, frame in enumerate(frames)]


class Rsi:
    _cache = {}

    def __init__(self, path):
        self.path = path
        with open(os.path.join(path, "meta.json"), encoding="utf-8-sig") as f:
            self.meta = json.load(f)
        self.size = (self.meta["size"]["x"], self.meta["size"]["y"])
        self.states = {state["name"]: state for state in self.meta["states"]}
        self._frames = {}

    @classmethod
    def load(cls, rel):
        rel = rel.strip("/").replace("\\", "/")
        if rel.lower().startswith("textures/"):
            rel = rel[len("textures/"):]
        if rel not in cls._cache:
            cls._cache[rel] = cls(os.path.join(TEXTURES, rel))
        return cls._cache[rel]

    def first_frames(self, state):
        """The first frame of each of the four facings (a one-direction state repeats its frame)."""
        if state in self._frames:
            return self._frames[state]
        meta = self.states[state]
        directions = meta.get("directions", 1)
        delays = meta.get("delays")
        frames = len(delays[0]) if delays else 1
        with Image.open(os.path.join(self.path, state + ".png")) as image:
            sheet = image.convert("RGBA")
        width, height = self.size
        columns = max(1, sheet.width // width)
        out = []
        for direction in range(4):
            index = (direction if directions >= 4 else 0) * frames
            x, y = (index % columns) * width, (index // columns) * height
            out.append(sheet.crop((x, y, x + width, y + height)))
        self._frames[state] = out
        return out

    def masks(self, state, behind=False):
        return silhouette(self.first_frames(state), behind)


class Existing:
    """One marking prototype that is already in the repository, as silhouettes grouped by colour box."""

    def __init__(self, data):
        self.id = data["id"]
        self.category = str(data.get("markingCategory"))
        self.species = data.get("speciesRestriction")
        self.sprites = []
        for sprite in data.get("sprites") or []:
            if isinstance(sprite, dict) and "sprite" in sprite and "state" in sprite:
                self.sprites.append((str(sprite["sprite"]), str(sprite["state"])))
        self.links = {str(k): str(v) for k, v in (data.get("colorLinks") or {}).items()}
        self.behind = {str(k) for k, v in (data.get("layering") or {}).items() if str(v) == "TailBehind"}
        self.groups = []
        self.union = [0] * 8

    _CHANNEL = re.compile(r"(primary|secondary|tertiary)")

    def measure(self):
        by_state = {}
        for rsi_path, state in self.sprites:
            try:
                by_state[state] = Rsi.load(rsi_path).masks(state, state in self.behind)
            except (OSError, KeyError, ValueError):
                continue  # a marking that names art that is not there is measured by the art that is
        roots = {}
        for state in by_state:
            root = state
            seen = set()
            while root in self.links and root not in seen:
                seen.add(root)
                root = self.links[root]
            roots[state] = root
        # SS13-style state names carry their colour channel; the layers of one channel are one colour
        # in the game they came from, whether or not this repository links them.
        tokens = {root: self._CHANNEL.search(root) for root in set(roots.values())}
        by_channel = bool(tokens) and all(tokens.values())
        groups = {}
        for state, masks in by_state.items():
            root = roots[state]
            groups.setdefault(tokens[root].group(1) if by_channel else root, []).append(masks)
        self.groups = [g for g in (union_of(parts) for parts in groups.values()) if any(g[:4])]
        self.union = union_of(self.groups)


def prototype_files():
    for folder, _, names in os.walk(PROTOTYPES):
        for name in sorted(names):
            if name.endswith(".yml"):
                yield os.path.join(folder, name)


def load_prototypes(kinds):
    """Every prototype of the given kinds, as (data, path)."""
    out = []
    wanted = tuple("type: %s" % kind for kind in kinds)
    for path in prototype_files():
        with open(path, encoding="utf-8-sig") as f:
            text = f.read()
        if not any(w in text for w in wanted):
            continue
        try:
            docs = yaml.load(text, Loader=LooseLoader)
        except yaml.YAMLError:
            continue
        for entry in docs or []:
            if isinstance(entry, dict) and entry.get("type") in kinds:
                out.append((entry, path))
    return out


def existing_markings(skip_under=()):
    """The repository's marking prototypes, measured; those under the given prototype folders are left out."""
    skip = tuple(os.path.normpath(os.path.join(PROTOTYPES, s)) for s in skip_under)
    out = []
    for data, path in load_prototypes(("marking",)):
        if skip and os.path.normpath(path).startswith(skip):
            continue
        marking = Existing(data)
        marking.measure()
        if area(marking.union):
            out.append(marking)
    return out


LOCALE = os.path.join(ROOT, "Resources", "Locale", "en-US")


def marking_names():
    """What the picker calls each marking: {id: name}, from the marking-<id> strings."""
    names = {}
    line = re.compile(r"^marking-([A-Za-z0-9_]+) = (.+)$")
    for folder, _, files in os.walk(LOCALE):
        for file in files:
            if not file.endswith(".ftl"):
                continue
            with open(os.path.join(folder, file), encoding="utf-8-sig") as f:
                for text in f:
                    m = line.match(text.rstrip())
                    if m:
                        names.setdefault(m.group(1), m.group(2))
    return names


class Bodies:
    """The base body of every species: one silhouette per humanoid layer and facing, for each torso."""

    def __init__(self):
        self.species = {}
        base = {}
        sets = {}
        species = {}
        for data, _path in load_prototypes(("humanoidBaseSprite", "speciesBaseSprites", "species")):
            if data["type"] == "humanoidBaseSprite":
                base[data["id"]] = data.get("baseSprite")
            elif data["type"] == "speciesBaseSprites":
                sets[data["id"]] = data.get("sprites") or {}
            else:
                species[data["id"]] = data
        for species_id, data in species.items():
            layers = sets.get(data.get("sprites"))
            if not layers:
                continue
            body = {}
            for sex in ("Male", "Female"):
                masks = {}
                images = {}
                for layer in BODY_LAYERS:
                    sprite_id = layers.get(layer)
                    if not sprite_id:
                        continue
                    sprite = base.get(sprite_id + sex) or base.get(sprite_id)
                    if not isinstance(sprite, dict) or "sprite" not in sprite:
                        continue
                    try:
                        rsi = Rsi.load(sprite["sprite"])
                        images[layer] = rsi.first_frames(sprite["state"])
                        masks[layer] = rsi.masks(sprite["state"])
                    except (OSError, KeyError, ValueError):
                        continue
                body[sex] = (masks, images)
            if body.get("Male", ({}, {}))[0]:
                self.species[species_id] = body

    def masks(self, species, sex="Male"):
        return self.species[species][sex][0]

    def images(self, species, sex="Male"):
        return self.species[species][sex][1]

    def part(self, species, layers, sex="Male"):
        masks = self.masks(species, sex)
        return union_of([masks[layer] for layer in layers if layer in masks])

    def cover(self, species, layers, direction, size, sex="Male"):
        """An 'L' mask of where some of a species' layers are opaque in one facing, centred on a canvas."""
        mask = Image.new("L", size, 0)
        for layer in layers:
            frames = self.images(species, sex).get(layer)
            if frames is None:
                continue
            frame = frames[direction]
            alpha = frame.getchannel("A").point(lambda a: 255 if a >= ALPHA_MIN else 0)
            mask.paste(255, ((size[0] - frame.width) // 2, (size[1] - frame.height) // 2), alpha)
        return mask

    def portrait(self, species, direction, sex="Male", tint=(196, 164, 132, 255), size=(32, 32)):
        """The species' body in one facing, tinted, centred on a canvas of the given size."""
        canvas = Image.new("RGBA", size, (0, 0, 0, 0))
        for layer in BODY_LAYERS:
            frames = self.images(species, sex).get(layer)
            if frames is None:
                continue
            canvas.alpha_composite(tinted(frames[direction], tint),
                                   ((size[0] - frames[direction].width) // 2, (size[1] - frames[direction].height) // 2))
        return canvas


def tinted(image, tint):
    """An image multiplied by a colour, the way the engine tints a greyscale sprite."""
    image = image.convert("RGBA")
    out = ImageChops.multiply(image, Image.new("RGBA", image.size, tint))
    out.putalpha(image.getchannel("A"))
    return out
