"""Ports cybernetic limb art into the RSIs, base sprites and style prototypes the augments use.

Run from the repository root against a Meridian Rift checkout:

    python Tools/_WF/Augments/port.py --source <Meridian-Rift checkout>

Every robotic style Meridian Rift offers for augments becomes a style here, and so does each Einstein Engines
cyberlimb brand already in the game (composited from the layers of its markings). An SS13 leg is one sprite;
here it is a leg and a foot layer, cut at the row the upstream species cut theirs.

Every style gets digitigrade legs. Meridian Rift draws them for the cyborg and advanced styles; for the rest,
each row of the digitigrade cyborg leg samples the same row of the style's own leg, stretched or narrowed to
fit, so the plating keeps its colours and the leg takes the digitigrade outline.
"""
import argparse
import glob
import json
import os
import re
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "MeridianMarkings"))
import ss13  # noqa: E402

REPOSITORY = "https://github.com/Aphelion-Moon/Meridian-Rift"
TEXTURES = "Resources/Textures"
OUTPUT = "_WF/Augments"
PROTOTYPES = "Resources/Prototypes/_WF/Augments"
EE_MARKINGS = "Resources/Prototypes/_EE/Entities/Mobs/Customization"
EE_TEXTURES = "_EinsteinEngines/Mobs/Species/Cybernetics"

# Rows from here down are the foot, as the upstream species cut theirs.
FOOT_ROW = 29
DIGI_SHEET = "modular_nova/modules/digitigrade_cybernetics/icons/digitigrade_parts.dmi"

# Style id suffix -> (sheet, display name, tinted to the skin colour). Order is the creator's order.
MERIDIAN = {
    "Cyborg": ("icons/mob/augmentation/augments.dmi", "Cyborg", False),
    "Surplus": ("icons/mob/augmentation/surplus_augments.dmi", "Surplus", False),
    "Advanced": ("icons/mob/augmentation/advanced_augments.dmi", "Advanced", False),
    "Engineering": ("icons/mob/augmentation/augments_engineer.dmi", "Engineering", False),
    "Mining": ("icons/mob/augmentation/augments_mining.dmi", "Mining", False),
    "Security": ("icons/mob/augmentation/augments_security.dmi", "Security", False),
    "Scrappy": ("modular_nova/master_files/icons/mob/augmentation/scrappyipc.dmi", "Scrappy", False),
    "ScrappyTinted": ("modular_nova/master_files/icons/mob/augmentation/scrappyipc_greyscale.dmi", "Scrappy (skin tinted)", True),
    "Morpheus": ("modular_nova/master_files/icons/mob/augmentation/mcgipc.dmi", "Morpheus Cyberkinetics", False),
    "Bishop": ("modular_nova/master_files/icons/mob/augmentation/bshipc.dmi", "Bishop Cyberkinetics", False),
    "Bishop2": ("modular_nova/master_files/icons/mob/augmentation/bs2ipc.dmi", "Bishop Cyberkinetics 2.0", False),
    "E3N": ("modular_nova/master_files/icons/mob/augmentation/e3n.dmi", "E3N AI", False),
    "Hephaestus": ("modular_nova/master_files/icons/mob/augmentation/hsiipc.dmi", "Hephaestus Industries", False),
    "Hephaestus2": ("modular_nova/master_files/icons/mob/augmentation/hi2ipc.dmi", "Hephaestus Industries 2.0", False),
    "Shellguard": ("modular_nova/master_files/icons/mob/augmentation/sgmipc.dmi", "Shellguard Munitions Standard Series", False),
    "WardTakahashi": ("modular_nova/master_files/icons/mob/augmentation/wtmipc.dmi", "Ward-Takahashi Manufacturing", False),
    "Xion": ("modular_nova/master_files/icons/mob/augmentation/xmgipc.dmi", "Xion Manufacturing Group", False),
    "Xion2": ("modular_nova/master_files/icons/mob/augmentation/xm2ipc.dmi", "Xion Manufacturing Group 2.0", False),
    "ZengHu": ("modular_nova/master_files/icons/mob/augmentation/zhpipc.dmi", "Zeng-Hu Pharmaceuticals", False),
    "Mariinsky": ("modular_nova/master_files/icons/mob/augmentation/mariinskyipc.dmi", "Mariinsky Ballet Company", False),
    "Zhenkov": ("modular_nova/master_files/icons/mob/augmentation/zhenkovipc.dmi", "Zhenkov & Co. Foundries", False),
    "ZhenkovDark": ("modular_nova/master_files/icons/mob/augmentation/zhenkovipc_dark.dmi", "Zhenkov & Co. Foundries (dark)", False),
}
# Styles whose digitigrade legs Meridian Rift draws: style -> state prefix in DIGI_SHEET.
NATIVE_DIGI = {"Cyborg": "robotic", "Advanced": "adv_robotic"}

# Style id suffix -> (marking file, marking id stem, display name).
EE = {
    "EEBishop": ("bishop", "Bishop", "Bishop Cyberkinetics (classic)"),
    "EEHephaestus": ("hephiastos", "Hesphiastos", "Hephaestus Industries (classic)"),
    "EEMorpheus": ("morpheus", "Morpheus", "Morpheus Cyberkinetics (classic)"),
    "EEShellguard": ("shellguard", "Shellguard", "Shellguard Munitions (classic)"),
    "EEWardTakahashi": ("wardtakahashi", "Wardtakahashi", "Ward-Takahashi (classic)"),
    "EEXion": ("xion", "Xion", "Xion Manufacturing Group (classic)"),
    "EEZengHu": ("zenghu", "Zenghu", "Zeng-Hu Pharmaceuticals (classic)"),
}

# Humanoid layer -> RSI state written here.
LAYERS = {
    "Head": "head", "Chest": "torso",
    "LArm": "l_arm", "RArm": "r_arm", "LHand": "l_hand", "RHand": "r_hand",
    "LLeg": "l_leg", "RLeg": "r_leg", "LFoot": "l_foot", "RFoot": "r_foot",
}
LEG_LAYERS = ("LLeg", "RLeg", "LFoot", "RFoot")
ARM_LAYERS = ("LArm", "RArm", "LHand", "RHand")
# SS13 state (after "robotic_") -> the layers it fills.
SS13_PARTS = {"head": ("Head",), "chest": ("Chest",), "l_arm": ("LArm",), "r_arm": ("RArm",),
              "l_hand": ("LHand",), "r_hand": ("RHand",), "l_leg": ("LLeg", "LFoot"), "r_leg": ("RLeg", "RFoot")}



def _blank():
    return ss13.Image.new("RGBA", (32, 32))


def _sheet(cells):
    sheet = ss13.Image.new("RGBA", (64, 64))
    for i, cell in enumerate(cells):
        sheet.paste(cell, ((i % 2) * 32, (i // 2) * 32))
    return sheet


def _rows(cells, top, bottom):
    out = []
    for cell in cells:
        part = _blank()
        part.paste(cell.crop((0, top, 32, bottom)), (0, top))
        out.append(part)
    return out


def _runs(image, y):
    """The stretches of opaque pixels on a row, as (first, last) columns."""
    runs, start = [], None
    for x in range(image.width + 1):
        solid = x < image.width and image.getpixel((x, y))[3] > 0
        if solid and start is None:
            start = x
        elif not solid and start is not None:
            runs.append((start, x - 1))
            start = None
    return runs


def _over(cells, more):
    for cell, layer in zip(cells, more):
        cell.alpha_composite(layer)


def _rsi_cells(folder, state):
    sheet = ss13.Image.open(os.path.join(TEXTURES, folder, state + ".png")).convert("RGBA")
    return [sheet.crop(((i % 2) * 32, (i // 2) * 32, (i % 2) * 32 + 32, (i // 2) * 32 + 32)) for i in range(4)]


def _meridian_layers(dmi):
    """Humanoid layer -> four facings, from a Meridian augment sheet."""
    layers = {}
    for part, targets in SS13_PARTS.items():
        state = dmi.states.get("robotic_" + part)
        if state is None or state.dirs != 4:
            continue
        cells = [image.copy() for image in state.images[0]]
        if len(targets) == 1:
            layers[targets[0]] = cells
        else:
            layers[targets[0]] = _rows(cells, 0, FOOT_ROW)
            layers[targets[1]] = _rows(cells, FOOT_ROW, 32)
    return layers


def _ee_markings(path):
    """Marking id -> its (sprite, state) layers, read from an EE marking file."""
    markings, current = {}, None
    sprite = None
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            match = re.match(r"\s*(-\s*)?(id|sprite|state):\s*(\S+)", line)
            if not match:
                continue
            key, value = match.group(2), match.group(3)
            if key == "id":
                current = markings.setdefault(value, [])
            elif key == "sprite":
                sprite = value
            elif current is not None and sprite:
                current.append((sprite, value))
    return markings


def _ee_layers(file, stem):
    markings = _ee_markings(os.path.join(EE_MARKINGS, file + ".yml"))
    layers = {}
    for layer in LAYERS:
        parts = markings.get("CyberLimbsMarking%s%s" % (stem, layer))
        if not parts:
            continue
        cells = [_blank() for _ in range(4)]
        for sprite, state in parts:
            _over(cells, _rsi_cells(sprite, state))
        layers[layer] = cells
    return layers


def _digi_outlines(source):
    dmi = ss13.Dmi.load(os.path.join(source, DIGI_SHEET))
    return {side: [image.copy() for image in dmi.states["robotic_%s_leg_digi" % side].images[0]] for side in "lr"}


def _digi_layers(style, layers, source, outlines):
    """The digitigrade leg and foot layers for a style."""
    out = {}
    for side in "lr":
        leg, foot = side.upper() + "Leg", side.upper() + "Foot"
        if leg not in layers:
            continue
        if style in NATIVE_DIGI:
            dmi = ss13.Dmi.load(os.path.join(source, DIGI_SHEET))
            whole = [image.copy() for image in dmi.states["%s_%s_leg_digi" % (NATIVE_DIGI[style], side)].images[0]]
        else:
            planti = [_blank() for _ in range(4)]
            _over(planti, layers[leg])
            if foot in layers:
                _over(planti, layers[foot])
            whole = [_fit_onto(outline, cell) for outline, cell in zip(outlines[side], planti)]
        out[leg] = _rows(whole, 0, FOOT_ROW)
        out[foot] = _rows(whole, FOOT_ROW, 32)
    return out


# The body clothing is drawn for: each layer is trimmed to it so nothing shows past a sleeve or trouser leg.
HUMAN = "Mobs/Species/Human/parts.rsi"
HUMAN_DIGI = "_WF/LegStyle/digitigrade_human.rsi"
MASKS = {"Head": (HUMAN, ("head_m", "head_f")), "Chest": (HUMAN, ("torso_m", "torso_f"))}
MASKS.update({layer: (HUMAN, (LAYERS[layer],)) for layer in ("LArm", "RArm", "LHand", "RHand") + LEG_LAYERS})


def _masks():
    """Layer -> (plantigrade outline, digitigrade outline) per facing; a pixel counts if any outline state has it."""
    def outline(folder, states):
        cells = [_blank() for _ in range(4)]
        for state in states:
            _over(cells, _rsi_cells(folder, state))
        return cells
    masks = {}
    for layer, (folder, states) in MASKS.items():
        digi = outline(HUMAN_DIGI, states) if layer in LEG_LAYERS else None
        masks[layer] = (outline(folder, states), digi)
    return masks


def _fill_side_views(layers, masks):
    """Some sheets draw both legs of a side view in one leg's state and leave the other's empty; here each leg is
    its own layer. A facing a part leaves empty where the human body has it takes the other side's drawing, and the
    trim to each side's outline then splits the two."""
    for left, right in (("LArm", "RArm"), ("LHand", "RHand"), ("LLeg", "RLeg"), ("LFoot", "RFoot")):
        if left not in layers or right not in layers:
            continue
        for this, other in ((left, right), (right, left)):
            for i in range(4):
                if (layers[this][i].getbbox() is None and layers[other][i].getbbox() is not None
                        and masks[this][0][i].getbbox() is not None):
                    layers[this][i] = layers[other][i].copy()


def _align(cells, mask):
    """Some sheets draw a part a pixel or two off where SS14's body has it (a side view's far hand, the back view's
    arms), so the trim would cut most of it away. Each facing is moved by up to three pixels to where it covers the
    most of the outline; one that already fits stays put."""
    out = []
    for cell, outline in zip(cells, mask):
        solid = {(x, y) for y in range(32) for x in range(32) if cell.getpixel((x, y))[3] > 0}
        inside = {(x, y) for y in range(32) for x in range(32) if outline.getpixel((x, y))[3] > 0}
        if not solid or not inside:
            out.append(cell)
            continue
        best, best_fit = (0, 0), len(solid & inside)
        if best_fit >= len(solid) * 0.6:
            out.append(cell)
            continue
        for dy in range(-3, 4):
            for dx in range(-3, 4):
                fit = len({(x + dx, y + dy) for x, y in solid} & inside)
                if fit > best_fit:
                    best, best_fit = (dx, dy), fit
        if best == (0, 0):
            out.append(cell)
            continue
        moved = _blank()
        moved.paste(cell, best, cell)
        out.append(moved)
    return out


def _trim(cells, mask):
    """Clears every pixel the mask leaves bare."""
    out = []
    for cell, outline in zip(cells, mask):
        cell = cell.copy()
        for y in range(32):
            for x in range(32):
                if outline.getpixel((x, y))[3] == 0:
                    cell.putpixel((x, y), (0, 0, 0, 0))
        out.append(cell)
    return out


# ------------------------------------------------------------------------------------- body shapes

PROTOTYPE_ROOT = "Resources/Prototypes"
LEGSTYLE_SPRITES = "Resources/Prototypes/_WF/LegStyle/base_sprites.yml"
OPPOSITE = {"LArm": "RArm", "RArm": "LArm", "LHand": "RHand", "RHand": "LHand",
            "LLeg": "RLeg", "RLeg": "LLeg", "LFoot": "RFoot", "RFoot": "LFoot"}


def _blocks(path):
    text = open(path, encoding="utf-8-sig", errors="ignore").read().replace("\r", "")
    for block in re.split(r"\n(?=- type: )", "\n" + text):
        block = block.strip("\n")
        if block:
            yield block.splitlines()[0].strip(), block


def _body_sprites():
    """(base sprite id, layer) for every limb a roundstart species or a leg style draws, and base sprite -> (rsi, state)."""
    species, sets, bases = [], {}, {}
    for path in glob.glob(PROTOTYPE_ROOT + "/**/*.yml", recursive=True):
        for kind, block in _blocks(path):
            ident = re.search(r"^\s*id:\s*(\S+)", block, re.M)
            if not ident:
                continue
            if kind == "- type: species":
                start = re.search(r"roundStart:\s*(\w+)", block)
                sprites = re.search(r"^\s*sprites:\s*(\S+)", block, re.M)
                if sprites and not (start and start.group(1) == "false"):
                    species.append(sprites.group(1))
            elif kind == "- type: speciesBaseSprites" and "sprites:" in block:
                sets[ident.group(1)] = dict(re.findall(r"^\s+(\w+):\s*(\S+)\s*$", block.split("sprites:", 1)[1], re.M))
            elif kind == "- type: humanoidBaseSprite":
                rsi, state = re.search(r"sprite:\s*(\S+)", block), re.search(r"state:\s*(\S+)", block)
                if rsi and state:
                    bases[ident.group(1)] = (rsi.group(1), state.group(1))

    used = set()
    for name in species:
        for layer, base in sets.get(name, {}).items():
            if layer in OPPOSITE and base in bases:
                used.add((base, layer))
    for kind, block in _blocks(LEGSTYLE_SPRITES):
        ident = re.search(r"^\s*id:\s*(\S+)", block, re.M)
        layer = re.search(r"(LLeg|RLeg|LFoot|RFoot)", ident.group(1)) if ident else None
        if layer and ident.group(1) in bases:
            used.add((ident.group(1), layer.group(1)))
    return sorted(used), bases


def _solid(cell):
    return {(x, y) for y in range(32) for x in range(32) if cell.getpixel((x, y))[3] > 0}


# A limb that overlaps the human's (or the digitigrade human's) this much in every facing wears the human-shaped
# sprites; only clearly different bodies get their own.
CLOSE_ENOUGH = 0.4


def _close_enough(cells, mask):
    for cell, outline in zip(cells, mask):
        a, b = _solid(cell), _solid(outline)
        if (a or b) and len(a & b) / max(1, len(a | b)) < CLOSE_ENOUGH:
            return False
    return True


def _shapes(masks):
    """Shape id -> (layer, outline, base sprites) for each limb outline a body draws that is clearly unlike the
    human's own or its digitigrade legs; base sprites with the same outline share a shape."""
    used, bases = _body_sprites()
    shapes, by_outline = {}, {}
    for base, layer in used:
        rsi, state = bases[base]
        if not os.path.exists(os.path.join(TEXTURES, rsi, state + ".png")):
            continue
        outline = [_outline_of(cell) for cell in _rsi_cells(rsi, state)]
        if not any(cell.getbbox() for cell in outline):
            continue
        plain, digi = masks[layer]
        if _close_enough(outline, plain) or (digi and _close_enough(outline, digi)):
            continue
        key = (layer, b"".join(cell.tobytes() for cell in outline))
        if key not in by_outline:
            shape = "WFAugmentShape" + base.replace("WFMob", "").replace("Mob", "")
            by_outline[key] = shape
            shapes[shape] = (layer, outline, [])
        shapes[by_outline[key]][2].append(base)
    return shapes


def _outline_of(cell):
    """The cell's outline: opaque white wherever it draws."""
    out = _blank()
    for y in range(32):
        for x in range(32):
            if cell.getpixel((x, y))[3] > 0:
                out.putpixel((x, y), (255, 255, 255, 255))
    return out


def _fit_onto(outline, source):
    """`source` drawn into `outline`, one facing: rows map top to bottom across both shapes, and each row of the
    outline samples its source row stretched or narrowed to fit."""
    out = _blank()
    targets = [y for y in range(32) if _runs(outline, y)]
    sources = [y for y in range(32) if _runs(source, y)]
    if not targets or not sources:
        return out
    t0, t1, s0, s1 = targets[0], targets[-1], sources[0], sources[-1]
    for y in targets:
        sy = s0 + round((y - t0) * (s1 - s0) / max(1, t1 - t0))
        sy = min(sources, key=lambda row: abs(row - sy))
        runs, src = _runs(outline, y), _runs(source, sy)
        for index, (first, last) in enumerate(runs):
            left, right = src[index] if len(src) == len(runs) else (src[0][0], src[-1][1])
            length, width = last - first + 1, right - left + 1
            for k in range(length):
                column = left + k * width // length if length > width else (
                    left + k if k < (length + 1) // 2 else right - (length - 1 - k))
                out.putpixel((first + k, y), source.getpixel((column, sy)))
    return out


def _fitted(layers, layer, outline):
    """A style's layer redrawn onto another body's outline. A facing the style leaves empty borrows the other side's
    part in that facing, then any facing it draws."""
    cells = []
    for i in range(4):
        source = layers[layer][i]
        if source.getbbox() is None and OPPOSITE[layer] in layers:
            source = layers[OPPOSITE[layer]][i]
        if source.getbbox() is None:
            source = next((cell for cell in layers[layer] if cell.getbbox()), source)
        cells.append(_fit_onto(outline[i], source))
    return cells


def _write_rsi(name, states, copyright):
    folder = os.path.join(TEXTURES, OUTPUT, name)
    # States a shape no longer needs mustn't linger from an earlier run.
    shutil.rmtree(folder, ignore_errors=True)
    os.makedirs(folder)
    for state, cells in states.items():
        _sheet(cells).save(os.path.join(folder, state + ".png"))
    meta = {"version": 1, "license": "CC-BY-SA-3.0", "copyright": copyright, "size": {"x": 32, "y": 32},
            "states": [{"name": state, "directions": 4} for state in sorted(states)]}
    with open(os.path.join(folder, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(meta, indent=2) + "\n")
    return folder


def port(source):
    commit = subprocess.run(["git", "-C", source, "rev-parse", "HEAD"], capture_output=True, text=True,
                            check=True).stdout.strip()
    outlines = _digi_outlines(source)
    sprites = ["# Base sprites for the augment styles. Written by Tools/_WF/Augments/port.py; don't edit by hand.\n"]
    styles = ["# Augment styles. Written by Tools/_WF/Augments/port.py; don't edit by hand.\n"]
    locale = ["# Augment style names. Written by Tools/_WF/Augments/port.py; don't edit by hand.\n"]

    entries = []
    for style, (sheet, name, tinted) in MERIDIAN.items():
        layers = _meridian_layers(ss13.Dmi.load(os.path.join(source, sheet)))
        note = "Taken from Meridian Rift at %s/tree/%s (%s), which carries the art of NovaSector, Skyrat-tg and " \
               "tgstation. Each leg is cut into a leg and a foot." % (REPOSITORY, commit, sheet)
        if style in NATIVE_DIGI:
            note += " The digitigrade legs are %s from %s." % (NATIVE_DIGI[style] + "_*_leg_digi", DIGI_SHEET)
        else:
            note += " The digitigrade legs are these legs refit to the outline of robotic_*_leg_digi in %s." % DIGI_SHEET
        entries.append((style, name, tinted, layers, note))
    for style, (file, stem, name) in EE.items():
        layers = _ee_layers(file, stem)
        note = "Composited from the layers of the %s/%s cyberlimb markings (%s), whose art is credited there. " \
               "The digitigrade legs are these legs refit to the outline of robotic_*_leg_digi in %s from Meridian " \
               "Rift at %s/tree/%s." % (EE_MARKINGS, file + ".yml", EE_TEXTURES, DIGI_SHEET, REPOSITORY, commit)
        entries.append((style, name, False, layers, note))

    masks = _masks()
    shapes = _shapes(masks)
    shape_lines = ["# Limb outlines bodies draw that differ from the human's, each with the base sprites that draw it.\n"
                   "# Written by Tools/_WF/Augments/port.py; don't edit by hand.\n"]
    for shape, (layer, _, bases) in shapes.items():
        shape_lines.append("\n- type: wfAugmentShape\n  id: %s\n  baseSprites:\n%s" % (
            shape, "".join("  - %s\n" % base for base in sorted(bases))))
    print("%d body shapes" % len(shapes))
    for style, name, tinted, layers, note in entries:
        _fill_side_views(layers, masks)
        digi = _digi_layers(style, layers, source, outlines)
        layers = {layer: _trim(_align(cells, masks[layer][0]) if layer in ARM_LAYERS else cells, masks[layer][0])
                  for layer, cells in layers.items()}
        digi = {layer: _trim(cells, masks[layer][1]) for layer, cells in digi.items()}
        note += " Each layer is trimmed to the outline of %s (%s for digitigrade legs) so clothing covers it." % (
            HUMAN, HUMAN_DIGI)
        states = {LAYERS[layer]: cells for layer, cells in layers.items()}
        states.update({LAYERS[layer] + "_digi": cells for layer, cells in digi.items()})
        fitted = {}
        for shape, (layer, outline, _) in shapes.items():
            if layer in layers:
                fitted[shape] = _fitted(layers, layer, outline)
                states[LAYERS[layer] + "_" + shape[len("WFAugmentShape"):].lower()] = fitted[shape]
        note += " Each limb is also redrawn onto the outline of every species' and leg style's own limb that differs."
        rsi = style.lower() + ".rsi"
        print("wrote", _write_rsi(rsi, states, note))

        style_id = "WFAugmentStyle" + style
        loc = "wf-augment-style-" + re.sub(r"(?<!^)(?=[A-Z])", "-", style).lower()
        lines = ["\n- type: wfAugmentStyle\n  id: %s\n  name: %s\n" % (style_id, loc)]
        for key, group in (("sprites", layers), ("digitigrade", digi)):
            if not group:
                continue
            lines.append("  %s:\n" % key)
            for layer in LAYERS:
                if layer not in group:
                    continue
                suffix = "Digi" if key == "digitigrade" else ""
                sprite_id = "WFMobAugment%s%s%s" % (style, layer, suffix)
                lines.append("    %s: %s\n" % (layer, sprite_id))
                # Markings are drawn for flesh; on plating they would cover it.
                sprites.append("\n- type: humanoidBaseSprite\n  id: %s\n  matchSkin: %s\n  allowsMarkings: false\n  baseSprite:\n"
                               "    sprite: %s/%s\n    state: %s\n" % (
                                   sprite_id, "true" if tinted else "false", OUTPUT, rsi,
                                   LAYERS[layer] + ("_digi" if suffix else "")))
        if fitted:
            lines.append("  fitted:\n")
            for shape in fitted:
                layer = shapes[shape][0]
                sprite_id = "WFMobAugment%s%s" % (style, shape[len("WFAugmentShape"):])
                lines.append("    %s: %s\n" % (shape, sprite_id))
                sprites.append("\n- type: humanoidBaseSprite\n  id: %s\n  matchSkin: %s\n  allowsMarkings: false\n  baseSprite:\n"
                               "    sprite: %s/%s\n    state: %s\n" % (
                                   sprite_id, "true" if tinted else "false", OUTPUT, rsi,
                                   LAYERS[layer] + "_" + shape[len("WFAugmentShape"):].lower()))
        styles.append("".join(lines))
        locale.append("%s = %s\n" % (loc, name))

    os.makedirs(PROTOTYPES, exist_ok=True)
    for path, text in ((os.path.join(PROTOTYPES, "shapes.yml"), shape_lines),
                       (os.path.join(PROTOTYPES, "base_sprites.yml"), sprites),
                       (os.path.join(PROTOTYPES, "styles.yml"), styles),
                       ("Resources/Locale/en-US/_WF/Augments/styles.ftl", locale)):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("".join(text))
        print("wrote", path)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--source", required=True, help="Meridian Rift checkout")
    port(parser.parse_args().source)
