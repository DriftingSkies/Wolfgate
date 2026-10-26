"""Finds the Meridian Rift clothing Wolfgate already has.

Every worn state in the repository's RSIs is indexed by its south-facing frame. An item is already here when
its worn sprite, drawn in its default colours, matches one of them: exactly, or with the same outline and
colours within a small difference (the same art re-exported or lightly touched up). Items of Meridian Rift's
own that share a sprite are one item, under the first type that has it.
"""
import hashlib
import json
import os
import pickle

from PIL import Image, ImageChops

import source

# Mean per-channel difference, out of 255, below which two sprites with the same outline are the same art.
SAME_ART = 6.0
# Share of outline pixels two sprites may differ by and still have the same outline.
SAME_OUTLINE = 0.03


def normalize(image):
    """RGBA with fully transparent pixels zeroed, so their hidden colour doesn't matter."""
    image = image.convert("RGBA")
    alpha = image.getchannel("A").point(lambda a: 255 if a else 0)
    black = Image.new("RGBA", image.size, (0, 0, 0, 0))
    return Image.composite(image, black, alpha)


def exact_key(image):
    return hashlib.md5(normalize(image).tobytes()).hexdigest()


def outline_key(image):
    """A coarse key for candidates: the size and the box of the opaque pixels."""
    box = image.getchannel("A").getbbox()
    return (image.size, box)


def similar(a, b):
    if a.size != b.size:
        return False
    ma = a.getchannel("A").point(lambda v: 255 if v else 0)
    mb = b.getchannel("A").point(lambda v: 255 if v else 0)
    area = max(1, sum(1 for v in ma.getdata() if v))
    xor = ImageChops.difference(ma, mb)
    if sum(1 for v in xor.getdata() if v) / area > SAME_OUTLINE:
        return False
    diff = ImageChops.difference(normalize(a).convert("RGB"), normalize(b).convert("RGB"))
    total = sum(sum(px) for px, m in zip(diff.getdata(), ma.getdata()) if m)
    return total / (area * 3) <= SAME_ART


class Index:
    """The worn states already in Wolfgate, by south frame."""

    def __init__(self, textures, cache=None):
        self.exact = {}
        self.outline = {}
        self.images = {}
        if cache and os.path.exists(cache):
            with open(cache, "rb") as f:
                self.exact, self.outline, raw = pickle.load(f)
            self.images = {k: Image.frombytes("RGBA", size, data) for k, (size, data) in raw.items()}
            return
        for dirpath, dirnames, _ in os.walk(textures):
            for d in dirnames:
                if not d.endswith(".rsi") or "_WF" + os.sep + "MeridianClothing" in os.path.join(dirpath, d):
                    continue
                self._add(os.path.join(dirpath, d), textures)
        if cache:
            raw = {k: (img.size, img.tobytes()) for k, img in self.images.items()}
            with open(cache, "wb") as f:
                pickle.dump((self.exact, self.outline, raw), f)

    def _add(self, rsi, textures):
        meta = os.path.join(rsi, "meta.json")
        try:
            with open(meta, encoding="utf-8-sig") as f:
                data = json.load(f)
            w, h = data["size"]["x"], data["size"]["y"]
        except (OSError, ValueError, KeyError):
            return
        rel = os.path.relpath(rsi, textures).replace(os.sep, "/")
        for state in data.get("states", []):
            name = state.get("name", "")
            if not name.startswith("equipped-") and "-equipped-" not in name:
                continue
            png = os.path.join(rsi, name + ".png")
            try:
                sheet = Image.open(png).convert("RGBA")
            except OSError:
                continue
            if sheet.width < w or sheet.height < h:
                continue
            south = sheet.crop((0, 0, w, h))
            if south.getchannel("A").getbbox() is None:
                continue
            key = "%s/%s" % (rel, name)
            self.images[key] = south
            self.exact.setdefault(exact_key(south), key)
            self.outline.setdefault(outline_key(south), []).append(key)

    def match(self, image):
        """The Wolfgate state an image duplicates, or None."""
        found = self.exact.get(exact_key(image))
        if found:
            return found
        for key in self.outline.get(outline_key(image), []):
            if similar(image, self.images[key]):
                return key
        return None


def south(layers, colors):
    """The south-facing first frame of a sprite set, drawn in the given colours."""
    if not layers:
        return None
    return source.composite(layers, colors).frames[0][0]


def signature(item):
    """
    The item's art. Colours are left out: greyscale items that differ only in their default colours are one
    item, as recolouring gives each of the others.
    """
    h = hashlib.md5()
    for s in (item.icon, item.worn):
        for layer in s:
            for row in layer.frames:
                for img in row:
                    h.update(normalize(img).tobytes())
            h.update(str(layer.color).encode())
    return h.hexdigest()
