"""Decides which port candidates the repository already has.

Two questions are asked of a candidate and a marking, and either one settles it.

Is it the same art? The colour boxes cover the same pixels and the shading inside them agrees. This is
what finds art another fork took from the same SS13 sheet, trimmed or cut into layers its own way.

Does it look the same? The whole outlines agree, whatever the colour boxes and the shading under them
do. A snout drawn again with its tip as a second colour, a reshaded horn, a tail with a band baked in:
in the picker these sit beside the marking they were made from and read as the same thing twice. Only
art that carries its own colours is held apart by them, since a blue wing and a red one are two wings.
"""
import hashlib

from PIL import Image

import wolfgate as wg

# A candidate whose colour boxes overlap a marking's this closely, with matching shading, is that marking.
SAME = 0.90
# The same picture cut into colour boxes another way still counts when the whole outlines agree this closely.
WHOLE_SAME = 0.97
# Below this nothing is recorded as a near match.
NEAR = 0.60
# Shading agreement. Art that is tinted in game is compared by how its brightness varies once each
# picture is scaled to its own average, so a darker copy or a light reshade of one drawing agrees and
# a stripe baked into it does not. Art that carries its own colours is compared by colour distance.
SHADING_SAME = 0.16
COLOUR_SAME = 40.0
# Below this many pixels a facing there is no shading to speak of: a hand patch is a hand patch.
TINY = 30
# Whole outlines that overlap this closely look the same. Patches on a body part are a dozen pixels,
# where one pixel either way is the whole difference, so those are held to less.
LOOKS_SAME = 0.90
LOOKS_SAME_PATCH = 0.80


def candidate_groups(candidate):
    """Silhouettes of a candidate's colour boxes: a sprite and the sprites that follow its colour are one box."""
    groups = {}
    for sprite in candidate.sprites:
        masks = wg.silhouette(sprite.frames[0], sprite.behind)
        if not any(masks[:4]):
            continue
        root = sprite.parent or sprite
        groups.setdefault(id(root), []).append(masks)
    return [wg.union_of(parts) for parts in groups.values()]


def candidate_picture(candidate):
    """What a candidate looks like untinted, one image per facing on the comparison canvas."""
    out = []
    for direction in range(4):
        canvas = Image.new("RGBA", wg.CANVAS, (0, 0, 0, 0))
        for sprite in [s for s in candidate.sprites if s.behind] + [s for s in candidate.sprites if not s.behind]:
            canvas.alpha_composite(wg.on_canvas(sprite.frames[0][direction]))
        out.append(canvas)
    return out


def carries_colour(pictures):
    """Whether art has colours of its own, as against the greys that are tinted in game."""
    spread = n = 0
    for picture in pictures:
        box = picture.getchannel("A").getbbox()
        if box is None:
            continue
        data = picture.crop(box).tobytes()
        for i in range(0, len(data), 4):
            if data[i + 3] < wg.ALPHA_MIN:
                continue
            spread += max(data[i], data[i + 1], data[i + 2]) - min(data[i], data[i + 1], data[i + 2])
            n += 1
    return n > 0 and spread / n > 12.0


class Accepted:
    """A candidate the port keeps, shaped like wolfgate.Existing so later candidates are compared with it too."""

    def __init__(self, candidate, subject):
        self.candidate = candidate
        self.id = candidate.key
        self.groups = subject.groups
        self.union = subject.union
        self.picture = subject.picture
        self.behind_share = subject.behind_share


_pictures = {}
_coloured = {}


def coloured(marking):
    """Whether a marking's art carries its own colours; measured once."""
    if marking.id not in _coloured:
        _coloured[marking.id] = carries_colour(picture_of(marking))
    return _coloured[marking.id]


def picture_of(marking):
    if isinstance(marking, Accepted):
        return marking.picture
    if marking.id in _pictures:
        return _pictures[marking.id]
    out = [Image.new("RGBA", wg.CANVAS, (0, 0, 0, 0)) for _ in range(4)]
    ordered = ([s for s in marking.sprites if s[1] in marking.behind]
               + [s for s in marking.sprites if s[1] not in marking.behind])
    for rsi_path, state in ordered:
        try:
            frames = wg.Rsi.load(rsi_path).first_frames(state)
        except (OSError, KeyError, ValueError):
            continue
        for direction in range(4):
            out[direction].alpha_composite(wg.on_canvas(frames[direction]))
    _pictures[marking.id] = out
    return out


def shading(a_pictures, b_pictures):
    """
    How alike two pictures are where both are opaque: (relative brightness difference, mean colour
    distance). The first is the mean of |a / mean(a) - b / mean(b)|, zero for the same drawing at any
    overall brightness.
    """
    lum_a, lum_b = [], []
    distance = 0.0
    for a, b in zip(a_pictures, b_pictures):
        box_a, box_b = a.getchannel("A").getbbox(), b.getchannel("A").getbbox()
        if box_a is None or box_b is None:
            continue
        box = (max(box_a[0], box_b[0]), max(box_a[1], box_b[1]), min(box_a[2], box_b[2]), min(box_a[3], box_b[3]))
        if box[0] >= box[2] or box[1] >= box[3]:
            continue
        pa, pb = a.crop(box).tobytes(), b.crop(box).tobytes()
        for i in range(0, len(pa), 4):
            if pa[i + 3] < wg.ALPHA_MIN or pb[i + 3] < wg.ALPHA_MIN:
                continue
            lum_a.append(0.299 * pa[i] + 0.587 * pa[i + 1] + 0.114 * pa[i + 2])
            lum_b.append(0.299 * pb[i] + 0.587 * pb[i + 1] + 0.114 * pb[i + 2])
            distance += (abs(pa[i] - pb[i]) + abs(pa[i + 1] - pb[i + 1]) + abs(pa[i + 2] - pb[i + 2])) / 3.0
    n = len(lum_a)
    if n == 0:
        return 1.0, 255.0
    mean_a, mean_b = max(sum(lum_a) / n, 1.0), max(sum(lum_b) / n, 1.0)
    relative = sum(abs(x / mean_a - y / mean_b) for x, y in zip(lum_a, lum_b)) / n
    return relative, distance / n


class Match:
    def __init__(self, existing, score, union, relative, distance, same_shading, boxes, looks):
        self.existing = existing
        self.boxes = boxes            # colour boxes of the candidate; the other marking's are existing.groups
        self.score = score            # worst colour box overlap, both ways
        self.union = union            # overlap of the whole silhouettes
        self.relative = relative      # relative brightness difference
        self.distance = distance      # colour distance
        self.same_shading = same_shading
        self.looks = looks            # looks the same, whatever the colour boxes and shading do

    @property
    def same(self):
        """The same art: matching colour boxes, or the same picture cut into at least as many boxes."""
        if not self.same_shading:
            return False
        return self.score >= SAME or (self.union >= WHOLE_SAME and len(self.existing.groups) >= self.boxes)

    @property
    def rank(self):
        return (self.same, self.looks, self.same_shading,
                max(self.score, self.union if self.same_shading else 0.0), self.union)

    def describe(self):
        return "%s (%s: boxes %.2f, outline %.2f, shading %.2f, colour %.0f)" % (
            self.existing.id, "the same art" if self.same else "the same look" if self.looks else "near",
            self.score, self.union, self.relative, self.distance)


class Subject:
    """A candidate measured once, for comparing against many markings."""

    def __init__(self, candidate, patch=False):
        self.groups = candidate_groups(candidate)
        self.union = wg.union_of(self.groups)
        self.size = wg.area(self.union)
        self.picture = candidate_picture(candidate)
        self.own_colours = carries_colour(self.picture)
        # A patch on a body part: held to the looser outline when it is small.
        self.looks_at = LOOKS_SAME_PATCH if patch and self.size < TINY * 4 else LOOKS_SAME
        # How much of the art is drawn below the body: wings behind the mob and the same wings in
        # front of it are two markings, though the pixels are the same.
        behind = sum(wg.area(wg.silhouette(s.frames[0])) for s in candidate.sprites if s.behind)
        whole = sum(wg.area(wg.silhouette(s.frames[0])) for s in candidate.sprites)
        self.behind_share = behind / whole if whole else 0.0

    def matches(self, markings):
        """Markings this candidate could be, best first."""
        if not self.groups:
            return []
        found = []
        for marking in markings:
            other = wg.area(marking.union)
            if not other or min(self.size, other) / max(self.size, other) < 0.5:
                continue
            whole = wg.overlap(self.union, marking.union)
            if whole < 0.5:
                continue
            forward = min(max(wg.overlap(g, h) for h in marking.groups) for g in self.groups)
            backward = min(max(wg.overlap(g, h) for g in self.groups) for h in marking.groups)
            score = min(forward, backward)
            if max(score, whole) < NEAR:
                continue
            relative, distance = shading(self.picture, picture_of(marking))
            if self.size < TINY * 4:
                same = True
            elif self.own_colours:
                same = distance <= COLOUR_SAME
            else:
                same = relative <= SHADING_SAME
            # Art with colours of its own on either side only looks the same in the same colours: a
            # sock is not the painted foot of a prosthetic leg, whatever their outlines.
            looks = whole >= self.looks_at and (
                distance <= COLOUR_SAME or not (self.own_colours or coloured(marking)))
            found.append(Match(marking, score, whole, relative, distance, same, len(self.groups), looks))
        found.sort(key=lambda m: m.rank, reverse=True)
        return found


def fingerprint(candidate):
    """Identity of a candidate's art, for finding candidates that are pixel-for-pixel the same."""
    digest = hashlib.sha1()
    for sprite in candidate.sprites:
        digest.update(b"|behind" if sprite.behind else b"|top")
        digest.update(sprite.channel.encode())
        for row in sprite.frames:
            for frame in row:
                box = frame.getchannel("A").getbbox()
                if box is None:
                    digest.update(b"-")
                    continue
                # Centre-relative, so the same art on a larger canvas is still the same art.
                digest.update(repr((box[0] - frame.width // 2, box[1] - frame.height // 2)).encode())
                cropped = frame.crop(box)
                # Colour left under fully transparent pixels is not art.
                clean = Image.new("RGBA", cropped.size, (0, 0, 0, 0))
                clean.paste(cropped, mask=cropped.getchannel("A").point(lambda a: 255 if a else 0))
                digest.update(repr(clean.size).encode())
                digest.update(clean.tobytes())
    return digest.hexdigest()
