"""Classifies every worn uniform and outer suit sprite by shape, for ClothingLayerSystem.

The layering this feeds depends on what the art covers, so it is read from the art:

    drapes      A uniform with fabric at shoe height that isn't on the legs or feet: a skirt, gown, robe or baggy
                trousers. It draws over the shoes. Fitted trousers, tights and short skirts don't.
    encloses    An outer suit that covers the whole body (torso, arms and legs) from every side: a hardsuit, EVA
                suit, bomb or fire suit, a full robe. The jumpsuit under it isn't drawn, so nothing pokes out.

Shapes are measured against the human body sprite, one frame per direction, all colour layers together. Sprites it
gets wrong are corrected by hand in overrides.yml next to this script. The result is written to
Resources/Prototypes/_WF/ClothingLayer/shapes.yml, keyed by RSI path.

    python Tools/_WF/ClothingLayer/shapes.py --write    regenerate the list
    python Tools/_WF/ClothingLayer/shapes.py --check    fail if the list is stale
"""

import argparse
import glob
import json
import math
import os
import re
import sys

import yaml
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OVERRIDES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "overrides.yml")
TEXTURES = os.path.join(ROOT, "Resources", "Textures")
OUTPUT = os.path.join(ROOT, "Resources", "Prototypes", "_WF", "ClothingLayer", "shapes.yml")
BODY = os.path.join(TEXTURES, "Mobs", "Species", "Human", "parts.rsi")
TILE = 32

# Shoe height starts here: knee boots reach it, the hips sit above it.
SHOE_TOP = 26
# Fabric this far outside the legs, or in the gap between them, is draped rather than worn on the leg.
GAP_X, FLARE_LEFT, FLARE_RIGHT = 15, 9, 21
DRAPE_PIXELS = 2
ENCLOSE_COVERAGE = 0.98


def _states(meta, slot):
    """The state names drawing a slot's art without prefix or species variants: plain or greyscale layers."""
    pattern = re.compile(r"equipped-%s(-\d+(-c\d+)?)?" % slot)
    return [s for s in meta["states"] if pattern.fullmatch(s["name"])]


def _frames(folder, meta, state):
    """The first animation frame of each direction, as alpha masks on tile coordinates."""
    path = os.path.join(folder, state["name"] + ".png")
    if not os.path.exists(path):
        return []
    w, h = meta["size"]["x"], meta["size"]["y"]
    dirs = state.get("directions", 1)
    count = len(state["delays"][0]) if state.get("delays") else 1
    with Image.open(path) as image:
        sheet = image.convert("RGBA")
    columns = max(1, sheet.width // w)
    ox, oy = (w - TILE) // 2, (h - TILE) // 2
    masks = []
    for d in range(dirs):
        index = d * count
        x, y = (index % columns) * w, (index // columns) * h
        alpha = sheet.crop((x, y, x + w, y + h)).getchannel("A").getdata()
        masks.append({(i % w - ox, i // w - oy) for i, a in enumerate(alpha) if a})
    return masks


def _meta(folder):
    with open(os.path.join(folder, "meta.json"), encoding="utf-8-sig") as f:
        return json.load(f)


def _union(folder, meta, slot):
    """Per direction, every pixel any of the slot's states draws."""
    out = [set(), set(), set(), set()]
    for state in _states(meta, slot):
        for d, mask in enumerate(_frames(folder, meta, state)):
            out[d] |= mask
    if not any(out[1:]):
        out = [out[0]] * 4
    return out


def _body():
    meta = _meta(BODY)
    states = {s["name"]: s for s in meta["states"]}
    part = lambda name: _frames(BODY, meta, states[name])
    combine = lambda *names: [set().union(*(part(n)[d] for n in names)) for d in range(4)]
    return combine("torso_m", "torso_f", "l_arm", "r_arm", "l_leg", "r_leg"), combine("l_leg", "r_leg", "l_foot", "r_foot")


def classify():
    body, legs = _body()
    drapes, encloses = [], []
    for meta_path in glob.glob(os.path.join(TEXTURES, "**", "*.rsi", "meta.json"), recursive=True):
        folder = os.path.dirname(meta_path)
        try:
            meta = _meta(folder)
        except ValueError:
            continue
        rsi = os.path.relpath(folder, TEXTURES).replace(os.sep, "/")
        if _states(meta, "INNERCLOTHING"):
            art = _union(folder, meta, "INNERCLOTHING")
            # Front and back: from the side the legs overlap and there is no gap to fill.
            for d in (0, 1):
                draped = [p for p in art[d] if p[1] >= SHOE_TOP and p not in legs[d]
                          and (p[0] == GAP_X or p[0] <= FLARE_LEFT or p[0] >= FLARE_RIGHT)]
                if len(draped) >= DRAPE_PIXELS:
                    drapes.append(rsi)
                    break
        if _states(meta, "OUTERCLOTHING"):
            art = _union(folder, meta, "OUTERCLOTHING")
            if all(len(art[d] & body[d]) >= ENCLOSE_COVERAGE * len(body[d]) for d in range(4)):
                encloses.append(rsi)
    return _override("drapes", drapes), _override("encloses", encloses)


def _override(kind, measured):
    """The measured list with overrides.yml's corrections for it applied."""
    with open(OVERRIDES, encoding="utf-8") as f:
        section = (yaml.safe_load(f) or {}).get(kind) or {}
    add, remove = section.get("add") or {}, section.get("remove") or {}
    for rsi in list(add) + list(remove):
        if not os.path.isdir(os.path.join(TEXTURES, rsi)):
            sys.exit("shapes.py: overrides.yml %s names %s, which doesn't exist" % (kind, rsi))
    return sorted((set(measured) | set(add)) - set(remove))


def render(drapes, encloses):
    lines = [
        "# Generated by python Tools/_WF/ClothingLayer/shapes.py --write from the clothing sprites. Don't edit by hand.",
        "",
        "- type: wfClothingShapes",
        "  id: WFDefault",
        "  drapes:",
    ]
    lines += ["  - %s" % r for r in drapes]
    lines.append("  encloses:")
    lines += ["  - %s" % r for r in encloses]
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--write", action="store_true", help="regenerate the list")
    group.add_argument("--check", action="store_true", help="fail if the list is stale")
    args = parser.parse_args()

    drapes, encloses = classify()
    text = render(drapes, encloses)
    if args.write:
        os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
        with open(OUTPUT, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        print("shapes.py: %d drape, %d enclose." % (len(drapes), len(encloses)))
        return
    current = ""
    if os.path.exists(OUTPUT):
        with open(OUTPUT, encoding="utf-8") as f:
            current = f.read().replace("\r\n", "\n")
    if current != text:
        sys.exit("shapes.py: %s is stale; run python Tools/_WF/ClothingLayer/shapes.py --write"
                 % os.path.relpath(OUTPUT, ROOT))
    print("shapes.py: OK.")


if __name__ == "__main__":
    main()
