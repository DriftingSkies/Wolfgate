"""Turns Meridian Rift's sprite accessory and body marking datums into port candidates.

A candidate is one marking as Wolfgate will list it: the SS13 datum it came from and its sprites, each
already composited onto a canvas centred on the mob the way the engine centres an RSI. Nothing here
knows what Wolfgate already has; dedupe.py decides which candidates are new and port.py names them.
"""
import os
import re

from PIL import Image, ImageChops

import ss13

TILE = 32

# SS13 icon_state layer postfix -> where it draws. BEHIND is below the body; the rest are above it,
# listed from the lowest to the highest.
LAYER_DEFINES = {
    "EXTERNAL_FRONT": "FRONT",
    "EXTERNAL_ADJACENT": "ADJ",
    "EXTERNAL_BEHIND": "BEHIND",
    "EXTERNAL_FRONT_UNDER_CLOTHES": "FRONT_UNDER",
    "EXTERNAL_FRONT_OVER": "FRONT_OVER",
    "EXTERNAL_FRONT_ABOVE_HAIR": "FRONT_OVER_HAIR",
}
ABOVE_BODY = ("ADJ", "FRONT_UNDER", "FRONT", "FRONT_OVER", "FRONT_OVER_HAIR")
CHANNELS = ("primary", "secondary", "tertiary")
SPRITE_ORDER = CHANNELS + ("inner",)

USE_ONE_COLOR = 31
USE_MATRIXED_COLORS = 32

# Layers an overlay draws when its organ cannot be resolved from the datum.
FALLBACK_LAYERS = {
    "tail": ("FRONT", "BEHIND"),
    "ears": ("FRONT", "ADJ", "BEHIND"),
    "snout": ("ADJ", "FRONT"),
    "horns": ("FRONT", "ADJ", "BEHIND"),
    "frills": ("FRONT", "ADJ"),
    "wings": ("FRONT", "BEHIND", "ADJ"),
    "spines": ("FRONT", "ADJ", "BEHIND"),
    "fluff": ("FRONT", "ADJ"),
    "neck_acc": ("FRONT", "ADJ"),
    "head_acc": ("FRONT", "ADJ", "BEHIND"),
    "moth_antennae": ("FRONT", "BEHIND"),
    "ipc_antenna": ("ADJ",),
    "skrell_hair": ("FRONT", "ADJ"),
    "xenodorsal": ("FRONT", "BEHIND"),
    "xenohead": ("ADJ",),
    "caps": ("FRONT", "ADJ"),
    "pod_hair": ("FRONT_OVER", "FRONT_OVER_HAIR"),
}


class Sprite:
    """One sprite of a candidate: its frames, where it draws and how it is coloured."""

    def __init__(self, suffix, frames, delays, behind=False, channel="primary", tinted=True):
        self.suffix = suffix      # what sets its state apart within the marking: "", "_primary", "_inner"
        self.frames = frames      # [frame][dir] -> RGBA Image on the candidate's canvas
        self.delays = delays      # seconds per frame, or None for a still
        self.behind = behind      # drawn below the body
        self.channel = channel    # primary / secondary / tertiary / inner
        self.tinted = tinted      # False for art that carries its own colours
        self.parent = None        # the sprite this one takes its colour from
        self.layer = None         # humanoid layer, when not the marking's own
        self.state = None         # RSI state name, set when the candidate is named


class Candidate:
    def __init__(self, kind, typepath, name):
        self.kind = kind          # category key: tail, ears, ..., body
        self.typepath = typepath
        self.name = name          # the SS13 name
        self.icon = None          # source DMI, relative to the SS13 root
        self.icon_state = None
        self.sprites = []
        self.size = (TILE, TILE)
        self.species = None       # SS13 recommended species ids, or None for any
        self.zone = None          # body markings: head, chest, l_arm ... l_foot
        self.feminine = False     # body markings: the chest art drawn for the female torso
        self.default_color = None # "#RRGGBB", "secondary", "tertiary" or None for the body's colour
        self.wag_of = None        # the candidate this is the wagging variant of
        self.wag = None           # the wagging variant of this candidate
        self.whole = None         # body markings cut to a body part: the art before the cut

    @property
    def key(self):
        """What a decision in decisions.yml names: the typepath, plus the body part for a body marking."""
        if self.zone is None:
            return self.typepath
        return "%s#%s%s" % (self.typepath, self.zone, "_f" if self.feminine else "")


def alpha_bbox(image):
    return image.getchannel("A").getbbox()


def blank(size):
    return Image.new("RGBA", size, (0, 0, 0, 0))


def is_blank(frames):
    return all(alpha_bbox(image) is None for row in frames for image in row)


def place(cell, offset, size):
    """A source cell on a canvas of the given size whose centre is the centre of the mob's tile."""
    height = cell.size[1]
    x_off, y_off = offset
    canvas = blank(size)
    dx = (size[0] - TILE) // 2 + x_off
    dy = (size[1] - TILE) // 2 - (height - TILE + y_off)
    canvas.alpha_composite(cell, (max(dx, 0), max(dy, 0)), (max(-dx, 0), max(-dy, 0)))
    return canvas


def extent(cells, offset):
    """Half extents around the tile centre that the opaque pixels of the cells reach, as (x, y)."""
    half_x = half_y = TILE // 2
    x_off, y_off = offset
    for cell in cells:
        box = alpha_bbox(cell)
        if box is None:
            continue
        height = cell.size[1]
        left = box[0] + x_off - TILE // 2
        right = box[2] + x_off - TILE // 2
        # Image rows run down; the tile's top row is height - TILE + y_off.
        top = box[1] - (height - TILE + y_off) - TILE // 2
        bottom = box[3] - (height - TILE + y_off) - TILE // 2
        half_x = max(half_x, -left, right)
        half_y = max(half_y, -top, bottom)
    return half_x, half_y


def canvas_for(half_x, half_y):
    """The smallest of the sizes the port uses that holds the extents."""
    width = next(w for w in (32, 48, 64, 96, 128) if w // 2 >= half_x)
    height = next(h for h in (32, 48, 64, 96, 128) if h // 2 >= half_y)
    return width, height


def erase(image, cover):
    """The image with the pixels a covering image hides removed."""
    alpha = ImageChops.multiply(image.getchannel("A"), ImageChops.invert(cover.getchannel("A")))
    out = image.copy()
    out.putalpha(alpha)
    return out


def keep(image, mask):
    """The image with only the pixels under an 'L' mask kept."""
    out = image.copy()
    out.putalpha(ImageChops.multiply(image.getchannel("A"), mask))
    return out


def order_sprites(candidate):
    """Sprites above the body first, then those below it, each by colour channel; links re-pointed."""
    candidate.sprites.sort(key=lambda s: (s.behind, SPRITE_ORDER.index(s.channel)))
    tops = {s.channel: s for s in candidate.sprites if not s.behind}
    for sprite in candidate.sprites:
        sprite.parent = tops.get(sprite.channel) if sprite.behind else None


def match_structure(still, wag):
    """
    Gives a tail and its wagging variant the same sprites in the same order: wagging swaps one
    marking for the other and copies the colours across by index.
    """
    for a, b in ((still, wag), (wag, still)):
        have = {(s.behind, s.channel) for s in a.sprites}
        for sprite in b.sprites:
            if (sprite.behind, sprite.channel) in have:
                continue
            a.sprites.append(Sprite(sprite.suffix, [[blank(a.size) for _ in range(4)]], None,
                                    sprite.behind, sprite.channel, sprite.tinted))
    order_sprites(still)
    order_sprites(wag)


class Source:
    def __init__(self, root):
        self.root = root
        self.datums = ss13.load_datums(
            root, ("/datum/sprite_accessory", "/datum/body_marking", "/datum/bodypart_overlay", "/obj/item/organ"))
        self.problems = []

    # ---------------------------------------------------------------------------- helpers

    def dmi(self, rel):
        return ss13.Dmi.load(os.path.join(self.root, rel))

    def species_of(self, typepath):
        raw = self.datums.raw(typepath, "recommended_species")
        if raw is None or raw.strip() == "null":
            return None
        return sorted(set(re.findall(r"SPECIES_\w+", raw)))

    def layers_of(self, typepath, key):
        """The layer postfixes the accessory's overlay draws, lowest first."""
        datums = self.datums
        organ = datums.raw(typepath, "organ_type")
        found = None
        if organ and organ.startswith("/"):
            overlay = datums.raw(organ.strip(), "bodypart_overlay")
            if overlay and overlay.startswith("/"):
                raw = datums.raw(overlay.strip(), "layers")
                if raw:
                    found = [LAYER_DEFINES[name] for name in re.findall(r"EXTERNAL_\w+", raw) if name in LAYER_DEFINES]
        if not found:
            found = list(FALLBACK_LAYERS.get(key, ("FRONT", "ADJ", "BEHIND")))
        order = ("BEHIND",) + ABOVE_BODY
        return sorted(set(found), key=order.index)

    # ---------------------------------------------------------------- sprite accessories

    def accessories(self, key):
        """Every named, drawable accessory registered under a feature key, keyed the way the game does: by name."""
        datums = self.datums
        by_name = {}
        for typepath in datums.subtypes("/datum/sprite_accessory"):
            if datums.value(typepath, "key") != key:
                continue
            if "name" not in datums.types[typepath]:
                continue
            name = datums.value(typepath, "name")
            if not isinstance(name, str):
                continue
            by_name[name] = typepath
        out = []
        for name, typepath in by_name.items():
            if datums.value(typepath, "factual", True) is False:
                continue
            if datums.value(typepath, "erp_accessory", False) is True:
                continue
            out.append((name, typepath))
        return out

    def _states(self, typepath, key, layers, prefix=""):
        """
        The states the game would draw for an accessory, as {layer: [(channel, tinted, DmiState)]},
        following build_icon_state_nova: m_<key>_<icon_state>_<LAYER>[_<channel>].
        """
        datums = self.datums
        icon = datums.value(typepath, "icon")
        icon_state = datums.value(typepath, "icon_state")
        if not isinstance(icon, str) or not isinstance(icon_state, str):
            return None, {}
        try:
            dmi = self.dmi(icon)
        except (OSError, ValueError) as error:
            self.problems.append("%s: %s" % (typepath, error))
            return None, {}
        feature = prefix + (datums.value(typepath, "feature_key_override") or key)
        color_src = datums.value(typepath, "color_src", USE_ONE_COLOR)
        tinted = bool(color_src)
        has_inner = datums.value(typepath, "has_inner", False) is True
        result = {}
        if color_src == USE_MATRIXED_COLORS:
            # The game only draws the channels it finds under the plain key, on any layer.
            channels = [c for c in CHANNELS
                        if any("m_%s_%s_%s_%s" % (key, icon_state, post, c) in dmi.states
                               for post in LAYER_DEFINES.values())]
        else:
            channels = [None]
        for layer in layers:
            found = []
            for channel in channels:
                name = "m_%s_%s_%s" % (feature, icon_state, layer)
                if channel:
                    name += "_" + channel
                state = dmi.states.get(name)
                if state is not None:
                    found.append((channel or "primary", tinted, state))
            if has_inner:
                state = dmi.states.get("m_%sinner_%s_%s" % (feature, icon_state, layer))
                if state is not None:
                    found.append(("inner", False, state))
            if found:
                result[layer] = found
        return icon, result

    def _offset(self, typepath):
        """Where BYOND draws an oversized icon: centred when the datum says so, otherwise from the tile's corner."""
        datums = self.datums
        if datums.value(typepath, "center", False) is not True:
            return 0, 0
        dim_x = datums.value(typepath, "dimension_x", TILE)
        dim_y = datums.value(typepath, "dimension_y", TILE)
        if not isinstance(dim_x, (int, float)) or not isinstance(dim_y, (int, float)):
            return 0, 0
        return -int(round((dim_x - TILE) / 2)), -int(round((dim_y - TILE) / 2))

    def _stack(self, per_layer, offset, size):
        """
        Composites what the game stacks per layer into the port's sprites: one per colour channel
        above the body and one per channel below it. A pixel belongs to whatever the game draws
        last there, so a merged channel never covers art that used to sit over it.
        Returns [(behind, channel, tinted, frames, delays)].
        """
        order = ("BEHIND",) + ABOVE_BODY
        entries = []
        for layer in order:
            for channel, tinted, state in per_layer.get(layer, ()):
                entries.append((layer == "BEHIND", channel, tinted, state))
        if not entries:
            return []

        frame_count = max(state.frames for _, _, _, state in entries)
        delays = None
        for _, _, _, state in entries:
            if state.frames == frame_count and state.delays:
                delays = [d / 10.0 for d in state.delays[:frame_count]]
                break
        if frame_count > 1 and delays is None:
            delays = [0.1] * frame_count

        keys = []
        for behind, channel, tinted, _ in entries:
            if (behind, channel) not in [(b, c) for b, c, _ in keys]:
                keys.append((behind, channel, tinted))
        keys.sort(key=lambda k: (k[0], SPRITE_ORDER.index(k[1])))

        frames = {(b, c): [[blank(size) for _ in range(4)] for _ in range(frame_count)] for b, c, _ in keys}
        for behind, channel, _tinted, state in entries:
            for frame in range(frame_count):
                source_frame = state.images[frame % state.frames]
                for direction in range(4):
                    placed = place(source_frame[direction if state.dirs >= 4 else 0], offset, size)
                    if alpha_bbox(placed) is None:
                        continue
                    # Sprites the port draws after this one would cover it; the game drew them before it.
                    for b, c, _ in keys:
                        if b == behind and SPRITE_ORDER.index(c) > SPRITE_ORDER.index(channel):
                            frames[(b, c)][frame][direction] = erase(frames[(b, c)][frame][direction], placed)
                    frames[(behind, channel)][frame][direction].alpha_composite(placed)
        return [(b, c, t, frames[(b, c)], delays) for b, c, t in keys]

    def accessory_candidates(self, key):
        datums = self.datums
        out = []
        for name, typepath in self.accessories(key):
            layers = self.layers_of(typepath, key)
            icon, per_layer = self._states(typepath, key, layers)
            if not per_layer:
                self.problems.append("%s (%s): the game has no state to draw for it" % (typepath, name))
                continue
            offset = self._offset(typepath)

            wag_layers = {}
            if key == "tail":
                _, wag_layers = self._states(typepath, key, layers, prefix="wagging")

            cells = [cell for found in list(per_layer.values()) + list(wag_layers.values())
                     for _, _, state in found for frame in state.images for cell in frame]
            size = canvas_for(*extent(cells, offset))

            candidate = self._build(key, typepath, name, icon, per_layer, offset, size)
            if candidate is None:
                self.problems.append("%s (%s): every state is empty" % (typepath, name))
                continue
            out.append(candidate)

            if wag_layers:
                wag = self._build(key, typepath, name, icon, wag_layers, offset, size)
                if wag is not None:
                    wag.wag_of = candidate
                    candidate.wag = wag
                    match_structure(candidate, wag)
                    out.append(wag)
        return out

    def _build(self, key, typepath, name, icon, per_layer, offset, size):
        datums = self.datums
        color_src = datums.value(typepath, "color_src", USE_ONE_COLOR)
        stacked = [entry for entry in self._stack(per_layer, offset, size) if not is_blank(entry[3])]
        if not stacked:
            return None
        candidate = Candidate(key, typepath, name)
        candidate.icon = icon
        candidate.icon_state = datums.value(typepath, "icon_state")
        candidate.size = size
        candidate.species = self.species_of(typepath)
        channels = {c for _, c, _, _, _ in stacked if c != "inner"}
        for behind, channel, tinted, frames, delays in stacked:
            if channel == "inner":
                suffix = "_inner"
            elif len(channels) > 1 or color_src == USE_MATRIXED_COLORS:
                suffix = "_" + channel
            else:
                suffix = ""
            candidate.sprites.append(Sprite(suffix, frames, delays, behind, channel, tinted))
        order_sprites(candidate)
        default = (datums.raw(typepath, "default_color") or "").strip()
        if not color_src:
            candidate.default_color = "#FFFFFF"
        elif default.startswith('"#'):
            candidate.default_color = default.strip('"').upper()
        return candidate

    # ------------------------------------------------------------------------------- hair

    def hair_candidates(self, kind):
        """
        Hairstyles ("hair") or facial hair ("facial_hair"), one sprite each: the style's own state
        with its appendages - the ponytails and braids the game layers around headwear - laid over it.
        """
        datums = self.datums
        by_name = {}
        for typepath in datums.subtypes("/datum/sprite_accessory/" + kind):
            if "name" not in datums.types[typepath]:
                continue
            name = datums.value(typepath, "name")
            if isinstance(name, str):
                by_name[name] = typepath
        out = []
        for name, typepath in by_name.items():
            icon = datums.value(typepath, "icon")
            icon_state = datums.value(typepath, "icon_state")
            if not isinstance(icon, str) or not isinstance(icon_state, str):
                continue  # bald, shaved
            try:
                dmi = self.dmi(icon)
            except (OSError, ValueError) as error:
                self.problems.append("%s: %s" % (typepath, error))
                continue
            names = [icon_state]
            for var in ("hair_appendages_inner", "hair_appendages_outer"):
                names += re.findall(r'"([^"]+)"\s*=', datums.raw(typepath, var) or "")
            states = [dmi.states[n] for n in names if n in dmi.states]
            if icon_state not in dmi.states:
                self.problems.append("%s (%s): the game has no state to draw for it" % (typepath, name))
                continue
            # Tall styles are drawn low in their cell and lifted by y_offset.
            lift = datums.value(typepath, "y_offset", 0)
            offset = (0, lift if isinstance(lift, int) else 0)
            cells = [state.images[0][d if state.dirs >= 4 else 0] for state in states for d in range(4)]
            size = canvas_for(*extent(cells, offset))
            frames = [blank(size) for _ in range(4)]
            for state in states:
                for direction in range(4):
                    frames[direction].alpha_composite(
                        place(state.images[0][direction if state.dirs >= 4 else 0], offset, size))
            if is_blank([frames]):
                continue
            candidate = Candidate(kind, typepath, name)
            candidate.icon = icon
            candidate.icon_state = icon_state
            candidate.size = size
            candidate.species = self.species_of(typepath)
            candidate.sprites.append(Sprite("", [frames], None))
            out.append(candidate)
        return out

    # ---------------------------------------------------------------------- body markings

    ZONES = ("head", "chest", "l_arm", "r_arm", "l_hand", "r_hand", "l_leg", "r_leg")
    ZONE_FLAGS = {
        "head": "HEAD", "chest": "CHEST", "l_arm": "ARM_LEFT", "r_arm": "ARM_RIGHT",
        "l_hand": "HAND_LEFT", "r_hand": "HAND_RIGHT", "l_leg": "LEG_LEFT", "r_leg": "LEG_RIGHT",
    }
    DEFAULT_COLORS = {"DEFAULT_SECONDARY": "secondary", "DEFAULT_TERTIARY": "tertiary"}

    def body_markings(self):
        """Named body markings, the last definition of a name winning as in the game's name-keyed list."""
        datums = self.datums
        by_name = {}
        for typepath in datums.subtypes("/datum/body_marking"):
            if typepath.startswith("/datum/body_marking_set"):
                continue
            if "name" not in datums.types[typepath]:
                continue
            name = datums.value(typepath, "name")
            if isinstance(name, str):
                by_name[name] = typepath
        return list(by_name.items())

    def body_candidates(self):
        """One candidate per body part a marking draws on standing legs; digitigrade art has no body here to fit."""
        datums = self.datums
        out = []
        for name, typepath in self.body_markings():
            icon = datums.value(typepath, "icon")
            icon_state = datums.value(typepath, "icon_state")
            if not isinstance(icon, str) or not isinstance(icon_state, str):
                self.problems.append("%s (%s): no icon" % (typepath, name))
                continue
            try:
                dmi = self.dmi(icon)
            except (OSError, ValueError) as error:
                self.problems.append("%s: %s" % (typepath, error))
                continue
            flags = set(re.findall(r"\b[A-Z_]+\b", datums.raw(typepath, "affected_bodyparts", "") or ""))
            gendered = datums.value(typepath, "gendered", True) is not False
            default = (datums.raw(typepath, "default_color") or "").strip()
            if default.startswith('"#'):
                default_color = default.strip('"').upper()
            else:
                default_color = self.DEFAULT_COLORS.get(default)
            species = self.species_of(typepath)
            drawn = 0
            for zone in self.ZONES:
                if self.ZONE_FLAGS[zone] not in flags:
                    continue
                variants = []
                if zone == "chest" and gendered:
                    male = dmi.states.get("%s_chest_m" % icon_state)
                    female = dmi.states.get("%s_chest_f" % icon_state)
                    if male is not None:
                        variants.append((male, False))
                    if female is not None and (male is None or not _same_art(male, female)):
                        variants.append((female, male is not None))
                else:
                    state = dmi.states.get("%s_%s" % (icon_state, zone))
                    if state is None and zone == "head" and flags == {"HEAD"}:
                        # A head-only marking may name its one state outright (zone_icon_state overrides).
                        state = dmi.states.get(icon_state)
                    if state is not None:
                        variants.append((state, False))
                for state, feminine in variants:
                    cells = [state.images[0][d if state.dirs >= 4 else 0] for d in range(4)]
                    # Body marking sheets are drawn from the tile's corner, whatever their cell size.
                    size = canvas_for(*extent(cells, (0, 0)))
                    frames = [[place(cell, (0, 0), size) for cell in cells]]
                    if is_blank(frames):
                        continue
                    candidate = Candidate("body", typepath, name)
                    candidate.icon = icon
                    candidate.icon_state = icon_state
                    candidate.zone = zone
                    candidate.feminine = feminine
                    candidate.species = species
                    candidate.default_color = default_color
                    candidate.size = size
                    candidate.sprites.append(Sprite("", frames, None))
                    out.append(candidate)
                    drawn += 1
            if not drawn:
                self.problems.append("%s (%s): no art for standing legs" % (typepath, name))
        return out


def _same_art(a, b):
    for direction in range(min(a.dirs, b.dirs)):
        if ImageChops.difference(a.images[0][direction], b.images[0][direction]).getbbox() is not None:
            return False
    return a.dirs == b.dirs
