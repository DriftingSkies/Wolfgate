"""Ports leg art into the RSIs and base sprites the leg style prototypes point at.

Run from the repository root against a Meridian Rift checkout and a Starlight one:

    python Tools/_WG/LegStyle/port.py --source <Meridian-Rift checkout> --starlight <Starlight checkout>

The furred species wear Starlight's Vulpkanin legs, the others Meridian Rift's. Vulpkanin and Canine also
take the rest of Starlight's Vulpkanin body, written to the Species module's textures, and stand on those
legs by default; their plantigrade option is the DeltaV legs they had.

An SS13 leg is one sprite; here it is a leg and a foot layer, so each is cut at the row the upstream
species cut theirs. Every set is tinted to the species that wears it: its channels are scaled by how that
species' torso compares with the torso the legs were drawn for. Where the species' torso would leave a gap
above the new legs, the hip of the torso they were drawn for fills it, and the species' own leg after that.
Starlight's legs bring that whole hip with them: legs draw over the torso, and the torsos here are shaded
dark along the bottom for the legs they came with. The Vulpkanin body's torsos are cut below the hip to the
outline of the DeltaV ones for the same reason, so each stance's legs draw the hip they belong to.

Clothing is drawn for plantigrade legs and moved by displacement maps:
- Shoes get a map per paw shape. Row by row, every stretch of digitigrade leg samples the stretch of human
  leg on that row. Starlight's legs fit the shoe map Reptilians use, which Starlight ships too.
- Jumpsuits and outer clothing use the leg rows of the map Reptilians use, mended per paw shape: a leg
  pixel that map would leave bare samples the human body as a shoe does. Starlight's legs take
  Starlight's own outer clothing map instead, mended the same way. A species whose own map also
  reshapes the upper body gets a map with its rows above the hip and those leg rows below, or untouched leg
  rows for a plantigrade option.
"""
import argparse
import json
import os
import shutil
import statistics
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "MeridianMarkings"))
import ss13  # noqa: E402

REPOSITORY = "https://github.com/Aphelion-Moon/Meridian-Rift"
ICONS = "modular_nova/modules/bodyparts/icons"
# A set cut from Starlight's Vulpkanin instead of a Meridian sheet names this as its sheet.
STARLIGHT = "starlight"
STARLIGHT_REPOSITORY = "https://github.com/ss14Starlight/space-station-14"
STARLIGHT_VULPKANIN = "Resources/Textures/_Starlight/Mobs/Species/Vulpkanin"
# A set cut from the DeltaV Vulpkanin already in this repository names this as its sheet.
DELTAV = "deltav"
DELTAV_VULPKANIN = "_DV/Mobs/Species/Vulpkanin/parts.rsi"
# Starlight's Vulpkanin body without its legs, which Vulpkanin and Canine wear.
VULPKANIN_BODY = "_WG/Species/Mobs/Species/Vulpkanin/parts.rsi"
TEXTURES = "Resources/Textures"
OUTPUT = "_WG/LegStyle"
PROTOTYPES = "Resources/Prototypes/_WG/LegStyle/base_sprites.yml"
# Rows from here down are the foot.
FOOT_ROW = 29
# Rows from here down wear the shoe; the ones above it may borrow the species' own leg.
SHOE_ROW = 25
# Rows from here down are where the legs change a jumpsuit.
HIP_ROW = 22
# The legs shoes are drawn for.
PLANTIGRADE = "Mobs/Species/Human/parts.rsi"
# The jumpsuit map made for digitigrade legs.
DIGITIGRADE_MAP = ("_White/Mobs/Species/displacement.rsi", "jumpsuit")
LEG_STATES = ("l_leg", "r_leg", "l_foot", "r_foot")
LAYERS = (("LLeg", "l_leg"), ("RLeg", "r_leg"), ("LFoot", "l_foot"), ("RFoot", "r_foot"))

# RSI -> the Meridian sheet and limb it is cut from, the leg state suffix ("_digi" for digitigrade legs),
# the body it is tinted to, the id its base sprites take and the paw shape its clothing maps are made for.
SETS = {
    "digitigrade_human.rsi": ("human_parts_greyscale.dmi", "human", "_digi", "Mobs/Species/Human/parts.rsi", "HumanLegDigi", "human"),
    "digitigrade_vulpkanin.rsi": (STARLIGHT, None, None, VULPKANIN_BODY, "VulpkaninLegDigi", STARLIGHT),
    "plantigrade_vulpkanin.rsi": (DELTAV, None, None, VULPKANIN_BODY, "VulpkaninLegPlanti", None),
    "digitigrade_feroxi.rsi": ("aquatic_parts_greyscale.dmi", "aquatic", "_digi", "_DV/Mobs/Species/Feroxi/parts.rsi", "FeroxiLegDigi", "mammal"),
    "digitigrade_goblin.rsi": ("humanoid_parts_greyscale.dmi", "humanoid", "_digi", "_NF/Mobs/Species/Goblin/parts.rsi", "GoblinLegDigi", "human"),
    "digitigrade_rodentia.rsi": (STARLIGHT, None, None, "_DV/Mobs/Species/Rodentia/parts.rsi", "RodentiaLegDigi", STARLIGHT),
    "digitigrade_shadekin.rsi": ("shadekin_parts_greyscale.dmi", "shadekin", "_digi", "_Starlight/Mobs/Species/Shadekin/parts.rsi", "ShadekinLegDigi", "shadekin"),
    "digitigrade_skrell.rsi": ("humanoid_parts_greyscale.dmi", "humanoid", "_digi", "_RMC14/Mobs/Skrells/parts.rsi", "SkrellLegDigi", "human"),
    "digitigrade_slime.rsi": ("slime_parts_greyscale.dmi", "slime", "_digi", "Mobs/Species/Slime/parts.rsi", "SlimeLegDigi", "mammal"),
    "digitigrade_tajaran.rsi": (STARLIGHT, None, None, "_Goobstation/Mobs/Species/Tajaran/parts.rsi", "TajaranLegDigi", STARLIGHT),
    "digitigrade_thaven.rsi": ("humanoid_parts_greyscale.dmi", "humanoid", "_digi", "_Impstation/Mobs/Species/Thaven/parts.rsi", "ThavenLegDigi", "human"),
    "plantigrade_synth.rsi": ("synthliz_parts_greyscale.dmi", "synthliz", "", "_HL/Mobs/Species/Synth/parts.rsi", "SynthLegPlanti", None),
}

# Jumpsuit and outer clothing maps: state -> (map kept above the hip or None, paw shape of the leg rows or
# None for legs as drawn, the map those leg rows start from). OUTER as the map above the hip keeps
# Starlight's whole outer clothing map.
JUMPSUIT, OUTER = "jumpsuit", "outer"
SUIT_MAPS = {
    "suit_human": (None, "human", JUMPSUIT),
    "suit_mammal": (None, "mammal", JUMPSUIT),
    "suit_shadekin": (None, "shadekin", JUMPSUIT),
    "suit_starlight": (None, STARLIGHT, JUMPSUIT),
    "outerclothing_starlight": (OUTER, STARLIGHT, OUTER),
    "jumpsuit_female_human": (("Mobs/Species/Human/displacement.rsi", "jumpsuit-female"), "human", JUMPSUIT),
    "jumpsuit_female_mammal": (("Mobs/Species/Human/displacement.rsi", "jumpsuit-female"), "mammal", JUMPSUIT),
    "jumpsuit_thaven": (("_Impstation/Mobs/Species/Thaven/displacement.rsi", "jumpsuit"), "human", JUMPSUIT),
    "outerclothing_thaven": (("_Impstation/Mobs/Species/Thaven/displacement.rsi", "outerclothing_hardsuit"), "human", JUMPSUIT),
    "jumpsuit_reptilian_plantigrade_female": (("_White/Mobs/Species/displacement.rsi", "jumpsuit-female"), None, None),
    "jumpsuit_synth_plantigrade": (("_HL/Mobs/Species/Synth/displacement.rsi", "jumpsuit"), None, None),
    "outerclothing_synth_plantigrade": (("_HL/Mobs/Species/Synth/displacement.rsi", "outerclothing"), None, None),
}


def _cells(folder, states, root=TEXTURES):
    """The given states of an RSI laid over each other, one image per facing."""
    cells = [ss13.Image.new("RGBA", (32, 32)) for _ in range(4)]
    for state in states:
        sheet = ss13.Image.open(os.path.join(root, folder, state + ".png")).convert("RGBA")
        for i, cell in enumerate(cells):
            x, y = (i % 2) * 32, (i // 2) * 32
            cell.alpha_composite(sheet.crop((x, y, x + 32, y + 32)))
    return cells


def _over(*layers):
    out = ss13.Image.new("RGBA", layers[0].size)
    for layer in layers:
        out.alpha_composite(layer)
    return out


def _sheet(cells):
    sheet = ss13.Image.new("RGBA", (64, 64))
    for i, cell in enumerate(cells):
        sheet.paste(cell, ((i % 2) * 32, (i // 2) * 32))
    return sheet


def _strip(cells, top, bottom):
    """The four facings as a sheet, keeping only rows top..bottom."""
    parts = []
    for cell in cells:
        part = ss13.Image.new("RGBA", cell.size)
        part.paste(cell.crop((0, top, cell.width, bottom)), (0, top))
        parts.append(part)
    return _sheet(parts)


def _means(cells):
    """The mean of each channel over the opaque pixels."""
    pixels = [cell.getpixel((x, y)) for cell in cells for y in range(cell.height) for x in range(cell.width)]
    pixels = [p for p in pixels if p[3] > 0]
    return [sum(p[c] for p in pixels) / len(pixels) for c in range(4)]


def _medians(cells):
    """The median of each channel over the opaque pixels."""
    pixels = [cell.getpixel((x, y)) for cell in cells for y in range(cell.height) for x in range(cell.width)]
    pixels = [p for p in pixels if p[3] > 0]
    return [statistics.median(p[c] for p in pixels) for c in range(4)]


def _scale(image, gains):
    channels = [c.point(lambda v, g=g: min(255, round(v * g))) for c, g in zip(image.split(), gains)]
    return ss13.Image.merge("RGBA", channels)


def _torsos(folder):
    """The species' torsos, the male one first."""
    with open(os.path.join(TEXTURES, folder, "meta.json"), encoding="utf-8-sig") as f:
        states = {state["name"] for state in json.load(f)["states"]}
    return [_cells(folder, [state]) for state in ("torso_m", "torso_f", "torso") if state in states]


def _fill_hip(cell, own, other, torsos):
    """Copies the species' own leg pixels above the shoe that the new legs or one of its torsos leave bare."""
    for y in range(SHOE_ROW):
        for x in range(cell.width):
            pixel = own.getpixel((x, y))
            if pixel[3] == 0 or cell.getpixel((x, y))[3] > 0 or other.getpixel((x, y))[3] > 0:
                continue
            if any(torso.getpixel((x, y))[3] == 0 for torso in torsos):
                cell.putpixel((x, y), pixel)


def _fill_source_hip(sides, i, chest, torsos, whole):
    """Copies the hip of the torso the legs were drawn for: all of it, or only where our torsos leave it bare."""
    for y in range(HIP_ROW, SHOE_ROW):
        for x in range(chest.width):
            pixel = chest.getpixel((x, y))
            if pixel[3] == 0 or any(cells[i].getpixel((x, y))[3] > 0 for cells in sides.values()):
                continue
            if not whole and all(torso.getpixel((x, y))[3] > 0 for torso in torsos):
                continue
            # The pixel joins whichever leg is nearer on its row.
            reach = {side: min((abs(x - column) for column in range(chest.width)
                                if cells[i].getpixel((column, y))[3] > 0), default=chest.width)
                     for side, cells in sides.items()}
            sides[min(reach, key=reach.get)][i].putpixel((x, y), pixel)


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


def _fit(target, source, top):
    """The displacement map that draws clothing made for `source` on `target`, for one facing.

    Red is how far right of a pixel the clothing is sampled. Only `target` is drawn.
    """
    out = ss13.Image.new("RGBA", target.size)
    for y in range(top, target.height):
        targets, sources = _runs(target, y), _runs(source, y)
        if not sources:
            continue
        for index, (first, last) in enumerate(targets):
            # Two legs side by side pair up; seen from the side both wear the one leg that is drawn.
            if len(sources) == len(targets):
                left, right = sources[index]
            else:
                left, right = sources[0][0], sources[-1][1]
            length, width = last - first + 1, right - left + 1
            for k in range(length):
                if length > width:
                    column = left + k * width // length
                elif k < (length + 1) // 2:
                    # A narrower leg keeps the clothing's two edges and drops its middle.
                    column = left + k
                else:
                    column = right - (length - 1 - k)
                out.putpixel((first + k, y), (128 + column - (first + k), 128, 0, 255))
    return out


def _leg_rows(base, legs, body, plantigrade):
    """The digitigrade map's leg rows, mended so no pixel of these legs samples off the plantigrade body."""
    out = base.copy()
    fit = _fit(body, plantigrade, HIP_ROW)
    for y in range(HIP_ROW, out.height):
        for x in range(out.width):
            if legs.getpixel((x, y))[3] == 0:
                continue
            red, green, _, alpha = base.getpixel((x, y))
            column = x + red - 128
            covered = alpha > 0 and green == 128 and 0 <= column < out.width and plantigrade.getpixel((column, y))[3] > 0
            if not covered and fit.getpixel((x, y))[3] > 0:
                out.putpixel((x, y), fit.getpixel((x, y)))
    return out


def _suit_map(above, below):
    """One map's rows above the hip over another's leg rows; either may be left as drawn."""
    out = ss13.Image.new("RGBA", (32, 32), (128, 128, 0, 255))
    if above is not None:
        out.paste(above.crop((0, 0, above.width, HIP_ROW)), (0, 0))
    if below is not None:
        out.paste(below.crop((0, HIP_ROW, below.width, below.height)), (0, HIP_ROW))
    return out


def _write_meta(folder, copyright, states, srgb=True):
    meta = {"version": 1, "license": "CC-BY-SA-3.0", "copyright": copyright, "size": {"x": 32, "y": 32}}
    if not srgb:
        meta["load"] = {"srgb": False}
    meta["states"] = [{"name": state, "directions": 4} for state in sorted(states)]
    with open(os.path.join(folder, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(meta, indent=2) + "\n")


def _credit(folder):
    with open(os.path.join(TEXTURES, folder, "meta.json"), encoding="utf-8-sig") as f:
        return json.load(f)["copyright"].rstrip(".")


def _vulpkanin_body(vulpkanin, credit):
    """Writes Starlight's Vulpkanin body without legs, its torsos cut below the hip to the DeltaV outline."""
    folder = os.path.join(TEXTURES, VULPKANIN_BODY)
    os.makedirs(folder, exist_ok=True)
    states = ["head_m", "head_f", "l_arm", "r_arm", "l_hand", "r_hand"]
    for state in states:
        shutil.copyfile(os.path.join(vulpkanin, "parts.rsi", state + ".png"), os.path.join(folder, state + ".png"))
    for state in ("torso_m", "torso_f"):
        cells = _cells("parts.rsi", [state], vulpkanin)
        for cell, outline in zip(cells, _cells(DELTAV_VULPKANIN, [state])):
            for y in range(HIP_ROW, cell.height):
                for x in range(cell.width):
                    if outline.getpixel((x, y))[3] == 0:
                        cell.putpixel((x, y), (0, 0, 0, 0))
        _sheet(cells).save(os.path.join(folder, state + ".png"))
        states.append(state)
    _write_meta(folder, "%s The legs are left out and the torsos cut below the hip to the outline of %s; "
                        "_WG/LegStyle holds the legs." % (credit, DELTAV_VULPKANIN), states)
    print("wrote", folder)


def _commit(checkout):
    return subprocess.run(["git", "-C", checkout, "rev-parse", "HEAD"], capture_output=True, text=True,
                          check=True).stdout.strip()


def port(source, starlight):
    commit, starlight_commit = _commit(source), _commit(starlight)
    vulpkanin = os.path.join(starlight, STARLIGHT_VULPKANIN)
    plantigrade = _cells(PLANTIGRADE, LEG_STATES)
    maps = os.path.join(TEXTURES, OUTPUT, "displacement.rsi")
    os.makedirs(maps, exist_ok=True)
    map_states, paws = [], {}
    human = _torsos(PLANTIGRADE)[0]
    flat_body = [_over(legs, torso) for legs, torso in zip(plantigrade, human)]
    bases = {
        JUMPSUIT: _cells(DIGITIGRADE_MAP[0], [DIGITIGRADE_MAP[1]]),
        OUTER: _cells("displacement.rsi", ["outerClothing"], vulpkanin),
    }
    leg_rows = {}
    prototypes = ["# Base sprites for the leg styles. Art from Meridian Rift, Starlight and DeltaV; written by "
                  "Tools/_WG/LegStyle/port.py.\n"]
    starlight_credit = ("Taken from Starlight at %s/tree/%s (%s/parts.rsi), where it is credited: taken from "
                        "Occulus-Eris (https://github.com/Occulus-Server/Occulus-Eris) and modified by "
                        "discord:kuro_0001." % (STARLIGHT_REPOSITORY, starlight_commit, STARLIGHT_VULPKANIN))
    _vulpkanin_body(vulpkanin, starlight_credit)

    for name, (sheet, limb, legs, body, sprite, paw) in SETS.items():
        torsos = _torsos(body)
        if sheet == STARLIGHT:
            chest = _cells("parts.rsi", ["torso_m"], vulpkanin)
            sides = {side: _cells("parts.rsi", ["%s_leg" % side, "%s_foot" % side], vulpkanin) for side in "lr"}
            credit = starlight_credit
        elif sheet == DELTAV:
            chest = _cells(DELTAV_VULPKANIN, ["torso_m"])
            sides = {side: _cells(DELTAV_VULPKANIN, ["%s_leg" % side, "%s_foot" % side]) for side in "lr"}
            credit = "Taken from %s, where it is credited: %s." % (DELTAV_VULPKANIN, _credit(DELTAV_VULPKANIN))
        else:
            dmi = ss13.Dmi.load(os.path.join(source, ICONS, sheet))
            chest = (dmi.states.get(limb + "_chest_m") or dmi.states[limb + "_chest"]).images[0]
            sides = {side: dmi.states["%s_%s_leg%s" % (limb, side, legs)].images[0] for side in "lr"}
            credit = ("Taken from Meridian Rift at %s/tree/%s (%s/%s), which carries the art of NovaSector, "
                      "Skyrat-tg and tgstation. Each leg is cut into a leg and a foot." % (
                          REPOSITORY, commit, ICONS, sheet))
        # Legs cut from a Vulpkanin body bring its hip and are shaded like the fur here but for darker
        # outlines, which would drag a mean down.
        vulpine = sheet in (STARLIGHT, DELTAV)
        tone = _medians if vulpine else _means
        gains = [ours / theirs for ours, theirs in zip(tone(torsos[0]), tone(chest))]
        sides = {side: [_scale(cell, gains) for cell in cells] for side, cells in sides.items()}
        for i, cell in enumerate(chest):
            _fill_source_hip(sides, i, _scale(cell, gains), [torso[i] for torso in torsos], vulpine)

        folder = os.path.join(TEXTURES, OUTPUT, name)
        os.makedirs(folder, exist_ok=True)
        whole = [ss13.Image.new("RGBA", (32, 32)) for _ in range(4)]
        for side, other in ("lr", "rl"):
            cells = sides[side]
            # The Vulpkanin body has no legs of its own to borrow from.
            if os.path.exists(os.path.join(TEXTURES, body, "%s_leg.png" % side)):
                for i, own in enumerate(_cells(body, ["%s_leg" % side])):
                    _fill_hip(cells[i], own, sides[other][i], [torso[i] for torso in torsos])
            _strip(cells, 0, FOOT_ROW).save(os.path.join(folder, "%s_leg.png" % side))
            _strip(cells, FOOT_ROW, 32).save(os.path.join(folder, "%s_foot.png" % side))
            for image, cell in zip(whole, cells):
                image.alpha_composite(cell)
        _write_meta(folder, "%s Tinted to %s." % (credit, body), LEG_STATES)
        for layer, state in LAYERS:
            prototypes.append("\n- type: humanoidBaseSprite\n  id: WGMob%s\n  baseSprite:\n    sprite: %s/%s\n    state: %s\n" % (
                sprite.replace("Leg", layer), OUTPUT, name, state))
        print("wrote %s, gains %s" % (folder, " ".join("%.2f" % gain for gain in gains)))

        if paw is None:
            continue
        shape = [frozenset((x, y) for y in range(SHOE_ROW, 32) for x in range(32) if image.getpixel((x, y))[3] > 0)
                 for image in whole]
        if paw not in paws:
            paws[paw] = shape
            # Starlight's paws are the ones the Reptilian shoe map was drawn for.
            if paw != STARLIGHT:
                state = "shoes_" + paw
                _sheet([_fit(leg, flat, SHOE_ROW) for leg, flat in zip(whole, plantigrade)]).save(
                    os.path.join(maps, state + ".png"))
                map_states.append(state)
            for key, base in bases.items():
                leg_rows[paw, key] = [_leg_rows(cell, leg, _over(leg, torso), flat)
                                      for cell, leg, torso, flat in zip(base, whole, human, flat_body)]
        elif paws[paw] != shape:
            raise ValueError("%s is not shaped like the other legs that wear the %s maps" % (name, paw))

    for state, (top, paw, base) in SUIT_MAPS.items():
        if top is None:
            above = [None] * 4
        else:
            above = bases[top] if top in bases else _cells(top[0], [top[1]])
        below = leg_rows[paw, base] if paw else [None] * 4
        _sheet([_suit_map(a, b) for a, b in zip(above, below)]).save(os.path.join(maps, state + ".png"))
        map_states.append(state)
    _write_meta(maps,
                "Made for Wolfgate by Tools/_WG/LegStyle/port.py. Shoe maps come from the shape of the legs beside "
                "them. The others join a species' own map above the hip (%s) to the leg rows of Litogin's %s/%s, "
                "mended for each paw shape. outerclothing_starlight is the outerClothing map by deltaVelocity from "
                "Starlight at %s/tree/%s (%s/displacement.rsi), mended the same way." % (
                    ", ".join(sorted({top[0] for top, _, _ in SUIT_MAPS.values() if isinstance(top, tuple)})),
                    *DIGITIGRADE_MAP, STARLIGHT_REPOSITORY, starlight_commit, STARLIGHT_VULPKANIN),
                map_states, srgb=False)
    print("wrote", maps)

    with open(PROTOTYPES, "w", encoding="utf-8", newline="\n") as f:
        f.write("".join(prototypes))
    print("wrote", PROTOTYPES)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--source", required=True, help="Meridian Rift checkout")
    parser.add_argument("--starlight", required=True, help="Starlight checkout")
    args = parser.parse_args()
    port(args.source, args.starlight)
