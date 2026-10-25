"""Imports Meridian Rift's drawn rolled-down jumpsuit sprites for the suits we share with it.

Meridian Rift (an SS13 fork) draws a rolled-down `<state>_d` for many of its jumpsuits (for others `_d` only rolls
the sleeves; those are skipped). This matches every worn
jumpsuit state here against Meridian's worn states by pixels; where one matches closely and has a `_d` sibling, the
`_d` sprite is written to Resources/Textures/_WF/RolledSleeves/Meridian/<original rsi path>, under the original state
name. The game uses it for "Roll down jumpsuit" and falls back to the cut mask for everything else.

Download Meridian's worn uniform DMIs (icons/mob/clothing/under/*.dmi, modular_nova/**/mob/clothing/under/*.dmi and
the like, skipping _digi and species files) into one folder first, each named after its repository path with `/`
replaced by `_`, then:

    python Tools/_WF/RolledSleeves/import_meridian_rolldowns.py <dmi folder> [--min-score 0.95]
"""

import argparse
import json
import re
import shutil
import sys
from pathlib import Path

from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[3]
TEXTURES = ROOT / "Resources" / "Textures"
OUTPUT = TEXTURES / "_WF" / "RolledSleeves" / "Meridian"
STATE_RE = re.compile(r"^(?:[^-]+-)?equipped-INNERCLOTHING$")
FRAME = 32
COLOUR_TOLERANCE = 24
# A `_d` this close to its normal sprite isn't rolled down.
IDENTICAL = 0.9
# Rows of the south-facing chest a roll-down leaves bare.
CHEST_ROWS = range(12, 18)


def dmi_states(path):
    """Returns {state: 64x64 RSI-layout image} for every 4-direction, single-frame 32x32 state."""
    img = Image.open(path)
    desc = img.info.get("Description", "")
    if not re.search(r"width = 32\s+height = 32", desc):
        return {}
    img = img.convert("RGBA")
    cols = img.width // FRAME
    states, idx = {}, 0
    for m in re.finditer(r'state = "([^"]*)"\n((?:\t.*\n?)*)', desc):
        dirs = int(re.search(r"dirs = (\d+)", m.group(2)).group(1))
        frames = int(re.search(r"frames = (\d+)", m.group(2)).group(1))
        if dirs == 4 and frames == 1:
            out = Image.new("RGBA", (FRAME * 2, FRAME * 2))
            for d in range(4):
                n = idx + d
                tile = img.crop(((n % cols) * FRAME, (n // cols) * FRAME,
                                 (n % cols) * FRAME + FRAME, (n // cols) * FRAME + FRAME))
                out.paste(tile, ((d % 2) * FRAME, (d // 2) * FRAME))
            states.setdefault(m.group(1), out)
        idx += dirs * frames
    return states


def alpha_mask(img):
    return img.getchannel("A").point(lambda a: 255 if a else 0)


def count(mask):
    return mask.histogram()[255]


def score(a, b, a_mask, b_mask):
    """Share of pixels opaque in either image that are opaque in both and close in colour."""
    union = count(ImageChops.lighter(a_mask, b_mask))
    if union == 0:
        return 0.0
    both = ImageChops.darker(a_mask, b_mask)
    diff = ImageChops.difference(a.convert("RGB"), b.convert("RGB"))
    close = diff.convert("L").point(lambda v: 255 if v < COLOUR_TOLERANCE else 0)
    return count(ImageChops.darker(both, close)) / union


def covers_chest(img):
    """Whether the south-facing sprite still covers the middle of the chest."""
    return all(img.getpixel((FRAME // 2 - 1, y))[3] for y in CHEST_ROWS)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("dmis", type=Path)
    parser.add_argument("--min-score", type=float, default=0.95)
    args = parser.parse_args()

    candidates = []
    for dmi in sorted(args.dmis.glob("*.dmi")):
        states = dmi_states(dmi)
        for name, img in states.items():
            down = states.get(f"{name}_d")
            if down is None or name.endswith("_d"):
                continue
            # Suits Meridian can't adjust carry a `_d` copy of the normal sprite, and some suits' `_d` only rolls the
            # sleeves (the chest stays covered); neither is a roll-down, so those suits get the cut mask instead.
            mask = alpha_mask(img)
            if score(img, down, mask, alpha_mask(down)) > IDENTICAL or covers_chest(down):
                continue
            candidates.append((dmi.name, name, img, mask, down))
    print(f"{len(candidates)} Meridian suits with a rolled-down sprite")

    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)

    matched = 0
    for meta_path in sorted(TEXTURES.rglob("*.rsi/meta.json")):
        rsi = meta_path.parent
        if OUTPUT in rsi.parents:
            continue
        try:
            meta = json.loads(meta_path.read_text(encoding="utf-8-sig"))
        except ValueError:
            continue
        if meta.get("size") != {"x": FRAME, "y": FRAME}:
            continue
        found = []
        for state in meta.get("states", []):
            name = state.get("name", "")
            png = rsi / f"{name}.png"
            if not STATE_RE.match(name) or state.get("directions") != 4 or "delays" in state or not png.exists():
                continue
            ours = Image.open(png).convert("RGBA")
            if ours.size != (FRAME * 2, FRAME * 2):
                continue
            ours_mask = alpha_mask(ours)
            best = max(((score(ours, img, ours_mask, mask), dmi, mr, down) for dmi, mr, img, mask, down in candidates),
                       key=lambda c: c[0], default=None)
            if best and best[0] >= args.min_score:
                found.append((name, *best))
        if not found:
            continue
        rel = rsi.relative_to(TEXTURES)
        dest = OUTPUT / rel
        dest.mkdir(parents=True, exist_ok=True)
        sources = []
        for name, value, dmi, mr, down in found:
            down.save(dest / f"{name}.png", optimize=True)
            sources.append(f"{name} from {dmi} state {mr}_d")
            print(f"{value:.2f}  {rel.as_posix()}:{name}  <-  {dmi}:{mr}_d")
            matched += 1
        out_meta = {
            "version": 1,
            "license": "CC-BY-SA-3.0",
            "copyright": "Rolled-down sprites from Meridian Rift (github.com/Aphelion-Moon/Meridian-Rift), based on "
                         "tgstation: " + "; ".join(sources) + ".",
            "size": {"x": FRAME, "y": FRAME},
            "states": [{"name": name, "directions": 4} for name, *_ in found],
        }
        (dest / "meta.json").write_text(json.dumps(out_meta, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Imported {matched} rolled-down sprites")


if __name__ == "__main__":
    sys.exit(main())
