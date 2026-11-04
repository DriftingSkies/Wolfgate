#!/usr/bin/env python3
"""Converts DS13-2.0's necromorph art and audio into the Remade module.

    python Tools/_WF/Remade/port_assets.py [--ds13 C:/Users/jzo12/Documents/GitHub/DS13-2.0]
    python Tools/_WF/Remade/port_assets.py --only Mobs/lurker_malo.rsi Mobs/fennec.rsi

DMI -> RSI, one RSI per DMI with every state kept. A mob's limb states also get a composited `full` state (4 dirs) and,
where its lying limbs exist, a `dead` state, so a mob can draw as one layer until the limb system hides pieces of it.
Audio is downmixed to mono, because stereo sources don't spatialise. Reruns overwrite the output. `--only` converts the
named RSIs and leaves everything else (the audio too) alone. A few mobs exist only in DS13-1.0 (`RSIS_V1`).
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / "Tools/_WF/Genitals"))
import dmi  # noqa: E402

TEXTURES = REPO / "Resources/Textures/_WF/Remade"
AUDIO = REPO / "Resources/Audio/_WF/Remade"
COLLECTIONS = REPO / "Resources/Prototypes/_WF/Remade/sounds.yml"
SOURCE_URL = "https://github.com/DS-13-Dev-Team/DS13-2.0"
SOURCE_URL_V1 = "https://github.com/DS-13-Dev-Team/DS13-1.0"

# Limbs back to front. Limb files use `<prefix>_<limb>`, or the bare limb, and their lying forms add `_lying`.
LIMB_ORDER = ["tail", "l_leg", "r_leg", "chest", "torso", "l_arm", "r_arm", "head", "proboscis", "eyes"]

# dmi under deadspace/icons -> rsi under Textures/_WF/Remade
RSIS = {
    "necromorphs/slasher/fleshy.dmi": "Mobs/slasher.rsi",
    "necromorphs/slasher/carrion.dmi": "Mobs/slasher_carrion.rsi",
    "necromorphs/slasher/desiccated.dmi": "Mobs/slasher_desiccated.rsi",
    "necromorphs/slasher_enhanced.dmi": "Mobs/slasher_enhanced.rsi",
    "necromorphs/infector.dmi": "Mobs/infector.rsi",
    "necromorphs/infector_enhanced.dmi": "Mobs/infector_enhanced.rsi",
    "necromorphs/brute.dmi": "Mobs/brute.rsi",
    "necromorphs/brute_flesh.dmi": "Mobs/brute_flesh.rsi",
    "necromorphs/hunter.dmi": "Mobs/hunter.rsi",
    "necromorphs/ubermorph.dmi": "Mobs/undying.rsi",
    "necromorphs/spitter.dmi": "Mobs/spitter.rsi",
    "necromorphs/leaper.dmi": "Mobs/leaper.rsi",
    "necromorphs/leaper_enhanced.dmi": "Mobs/leaper_enhanced.rsi",
    "necromorphs/leaper_hopper.dmi": "Mobs/hopper.rsi",
    "necromorphs/exploder/exploder.dmi": "Mobs/exploder.rsi",
    "necromorphs/exploder/exploder_enhanced.dmi": "Mobs/exploder_enhanced.rsi",
    "necromorphs/puker/puker.dmi": "Mobs/puker.rsi",
    "necromorphs/puker/puker_flayed.dmi": "Mobs/puker_flayed.rsi",
    "necromorphs/lurker/lurker.dmi": "Mobs/lurker.rsi",
    "necromorphs/lurker/lurker_malo.dmi": "Mobs/lurker_malo.rsi",
    "necromorphs/divider.dmi": "Mobs/divider.rsi",
    "necromorphs/divider/components.dmi": "Mobs/divider_components.rsi",
    "necromorphs/divider/components_large.dmi": "Mobs/divider_components_large.rsi",
    "necromorphs/tripod.dmi": "Mobs/tripod.rsi",
    "necromorphs/twitcher.dmi": "Mobs/twitcher.rsi",
    "necromorphs/twitcher_oracle.dmi": "Mobs/twitcher_oracle.rsi",
    "necromorphs/guardian.dmi": "Mobs/guardian.rsi",
    "necromorphs/swarmer.dmi": "Mobs/swarmer.rsi",
    "necromorphs/swinging_limbs.dmi": "Effects/swinging_limbs.rsi",
    "necromorphs/necroprojectiles.dmi": "Effects/projectiles.rsi",
    "obj/marker_normal.dmi": "Structures/scion.rsi",
    "obj/marker_giant.dmi": "Structures/prime.rsi",
    "obj/marker_shard.dmi": "Objects/shard.rsi",
    "obj/necromorph_organ.dmi": "Objects/organs.rsi",
    "effects/new_corruption.dmi": "Structures/corruption.rsi",
    "effects/corruption.dmi": "Structures/growths.rsi",
    "effects/corruption64.dmi": "Structures/nest.rsi",
    "effects/corruption32x48.dmi": "Structures/growth_wall.rsi",
    "effects/corruption96x96.dmi": "Structures/growth_large.rsi",
    "effects/harvester_new.dmi": "Structures/harvester.rsi",
    "effects/runes.dmi": "Effects/runes.rsi",
    "hud/signal_icons.dmi": "Interface/signal_actions.rsi",
    "hud/action_corruption.dmi": "Interface/corruption_actions.rsi",
}

# dmi under DS13-1.0's icons -> rsi under Textures/_WF/Remade, for what DS13-2.0 never had
RSIS_V1 = {
    "mob/necromorph/leaper_fennec.dmi": "Mobs/fennec.rsi",
}

# Art with no lying limbs, which DS13 draws by turning the whole mob on its side. Each standing limb gets a `_lying` state
# turned a quarter, so the limb system can drop and draw limbs as it does for any other mob.
TURNED_LYING = {"Mobs/slasher_carrion.rsi"}

SOUNDS = [
    ("sound/effects/creatures/necromorph", ""),
    ("sound/effects/footstep", "footstep"),
    ("sound/effects/attacks", "attacks"),
]
SINGLE_SOUNDS = {
    "sound/effects/markerthrob.ogg": "monolith/throb.ogg",
    "sound/effects/biohazard_alarm.ogg": "monolith/alarm.ogg",
}
FOOTSTEP = re.compile(r"(brute|exploder|infector|leaper|lurker|puker|slasher|spitter|tripod|twitcher|ubermorph)", re.I)


def clean_state(name: str, used: set[str]) -> str:
    base = re.sub(r"[^A-Za-z0-9_\-]", "_", name) or "blank"
    state, n = base, 1
    while state in used:
        n += 1
        state = f"{base}_{n}"
    used.add(state)
    return state


def limb_of(state: str) -> tuple[str, bool] | None:
    lying = state.endswith("_lying")
    stem = state[: -len("_lying")] if lying else state
    for limb in LIMB_ORDER:
        if stem == limb or stem.endswith("_" + limb):
            return limb, lying
    return None


def composite(states: dict[str, dict], lying: bool) -> list[Image.Image] | None:
    layers: dict[str, list[Image.Image]] = {}
    for name, st in states.items():
        found = limb_of(name)
        if found and found[1] == lying and found[0] not in layers:
            layers[found[0]] = st["img"][0]
    if "chest" not in layers and "torso" not in layers:
        return None
    dirs = 1 if lying else 4
    if not lying and any(len(v) == 1 for k, v in layers.items() if k in ("chest", "torso")):
        return None
    w, h = next(iter(layers.values()))[0].size
    out = []
    for d in range(dirs):
        canvas = Image.new("RGBA", (w, h))
        for limb in LIMB_ORDER:
            frames = layers.get(limb)
            if frames:
                frame = frames[d if d < len(frames) else 0]
                canvas.alpha_composite(frame)
        out.append(canvas)
    return out


def write_state(rsi: Path, name: str, frames: list[list[Image.Image]], delay: list[float] | None) -> dict:
    """frames[frame][dir]; an RSI png holds a direction's frames together, wrapped to a near square."""
    dirs = len(frames[0])
    count = len(frames) * dirs
    w, h = frames[0][0].size
    cols = 1
    while cols * cols < count:
        cols += 1
    rows = (count + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * w, rows * h))
    i = 0
    for d in range(dirs):
        for frame in frames:
            sheet.paste(frame[d], ((i % cols) * w, (i // cols) * h))
            i += 1
    sheet.save(rsi / f"{name}.png")
    meta: dict = {"name": name}
    if dirs > 1:
        meta["directions"] = dirs
    if len(frames) > 1:
        ticks = delay if delay and len(delay) == len(frames) else [1.0] * len(frames)
        meta["delays"] = [[round(t * 0.1, 3) for t in ticks] for _ in range(dirs)]
    return meta


def turn_lying(by_name: dict[str, dict], metas: list[dict], rsi: Path) -> None:
    """Adds `<limb>_lying` for each standing limb that lacks one: its south frame turned a quarter clockwise."""
    for name, st in list(by_name.items()):
        found = limb_of(name)
        lying = f"{name}_lying"
        if not found or found[1] or lying in by_name:
            continue
        frame = st["img"][0][0].rotate(-90)
        by_name[lying] = {"name": lying, "img": [[frame]]}
        metas.append(write_state(rsi, lying, [[frame]], None))


def convert_dmi(src: Path, rel: str, dest: Path, source_url: str = SOURCE_URL, repo: str = "DS13-2.0", prefix: str = "deadspace/icons/") -> None:
    width, height, loaded = dmi.load_dmi(src)

    rsi = TEXTURES / dest
    if rsi.exists():
        shutil.rmtree(rsi)
    rsi.mkdir(parents=True)

    used: set[str] = set()
    by_name: dict[str, dict] = {}
    metas = []
    for st in loaded:
        name = clean_state(st["name"], used)
        by_name[name] = st
        # A state with other than 1, 4 or 8 dirs can't be an RSI state; none of these files use one.
        metas.append(write_state(rsi, name, st["img"], st.get("delay")))

    if dest.as_posix() in TURNED_LYING:
        turn_lying(by_name, metas, rsi)
        used.update(by_name)

    if str(dest).startswith("Mobs"):
        full = composite(by_name, False)
        if full and "full" not in used:
            metas.append(write_state(rsi, "full", [full], None))
        dead = composite(by_name, True)
        if dead and "dead" not in used:
            metas.append(write_state(rsi, "dead", [dead], None))

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": f"Taken from {repo} at {source_url} ({prefix}{rel})",
        "size": {"x": width, "y": height},
        "states": metas,
    }
    (rsi / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")


def to_mono(src: Path, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    channels = subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=channels", "-of", "csv=p=0", str(src)],
        capture_output=True, text=True, check=True).stdout.strip()
    if channels == "1":
        shutil.copyfile(src, dest)
        return
    subprocess.run(
        ["ffmpeg", "-y", "-v", "error", "-i", str(src), "-ac", "1", "-c:a", "libvorbis", "-q:a", "4", str(dest)],
        check=True)


def port_audio(root: Path) -> None:
    if AUDIO.exists():
        shutil.rmtree(AUDIO)
    files: list[str] = []
    for sub, prefix in SOUNDS:
        base = root / sub
        for src in sorted(base.rglob("*.ogg")):
            if prefix == "footstep" and not FOOTSTEP.search(src.name):
                continue
            rel = src.relative_to(base).as_posix()
            out = f"{prefix}/{rel}" if prefix else rel
            out = out.replace("ubermorph", "undying")
            to_mono(src, AUDIO / out)
            files.append(out)
    for sub, out in SINGLE_SOUNDS.items():
        to_mono(root / sub, AUDIO / out)
        files.append(out)

    lines = []
    for out in sorted(files):
        lines += [
            f'- files: ["{out}"]',
            '  license: "CC-BY-SA-3.0"',
            '  copyright: "Taken from DS13-2.0, downmixed to mono"',
            f'  source: "{SOURCE_URL}"',
            "",
        ]
    (AUDIO / "attributions.yml").write_text("\n".join(lines), encoding="utf-8", newline="\n")
    write_collections(files)


def write_collections(files: list[str]) -> None:
    """One soundCollection per mob and sound kind, from names like `slasher/slasher_shout_long_2.ogg`."""
    groups: dict[str, list[str]] = {}
    for out in files:
        path = Path(out)
        if path.parts[0] in ("attacks", "monolith"):
            continue
        stem = re.sub(r"_?\d+$", "", path.stem)
        if not stem:
            continue
        key = "".join(p.capitalize() for p in re.split(r"[_\-/]", stem) if p)
        if path.parts[0] != "footstep" and not stem.startswith(path.parts[0].split("/")[0]):
            key = "".join(p.capitalize() for p in path.parts[0].split("_")) + key
        groups.setdefault(key.replace("SlasherEnhancedEslasher", "SlasherEnhanced"), []).append(out)

    COLLECTIONS.parent.mkdir(parents=True, exist_ok=True)
    lines = ["# Generated by Tools/_WF/Remade/port_assets.py.", ""]
    for key in sorted(groups):
        lines += ["- type: soundCollection", f"  id: WFRemade{key}", "  files:"]
        lines += [f"  - /Audio/_WF/Remade/{f}" for f in sorted(groups[key])]
        lines.append("")
    COLLECTIONS.write_text("\n".join(lines), encoding="utf-8", newline="\n")


def make_wall_growth() -> None:
    """
    Growth for wall faces, built from DS13-2.0's corruption `edge` piece (new_corruption.dmi, already in corruption.rsi).
    DS13 has no wall overlay of its own, so one band of the edge is laid on each side of a wall that corruption touches.
    State `wall-<mask>` takes N 1, S 2, E 4, W 8 for the sides with corruption next to them.
    """
    src = TEXTURES / "Structures/corruption.rsi/edge.png"
    # edge.png is solid along its east side, so turning it points that side north, south or west.
    bands = {1: Image.open(src).convert("RGBA").rotate(90), 2: Image.open(src).convert("RGBA").rotate(-90),
             4: Image.open(src).convert("RGBA"), 8: Image.open(src).convert("RGBA").rotate(180)}

    rsi = TEXTURES / "Structures/wall_growth.rsi"
    if rsi.exists():
        shutil.rmtree(rsi)
    rsi.mkdir(parents=True)

    states = []
    for mask in range(1, 16):
        canvas = Image.new("RGBA", (32, 32))
        for bit, band in bands.items():
            if mask & bit:
                canvas.alpha_composite(band)
        canvas.save(rsi / f"wall-{mask}.png")
        states.append({"name": f"wall-{mask}"})

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": f"Edge piece taken from DS13-2.0 at {SOURCE_URL} (deadspace/icons/effects/new_corruption.dmi), rotated and combined by Tools/_WF/Remade/port_assets.py",
        "size": {"x": 32, "y": 32},
        "states": states,
    }
    (rsi / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ds13", default="C:/Users/jzo12/Documents/GitHub/DS13-2.0")
    parser.add_argument("--ds13-v1", default="C:/Users/jzo12/Documents/GitHub/DS13-1.0")
    parser.add_argument("--only", nargs="+", help="RSIs to convert, like Mobs/fennec.rsi; skips the audio")
    parser.add_argument("--wall-growth-only", action="store_true", help="rebuild only Structures/wall_growth.rsi from corruption.rsi")
    args = parser.parse_args()
    root = Path(args.ds13) / "deadspace"
    only = set(args.only) if args.only else None

    if args.wall_growth_only:
        make_wall_growth()
        return

    count = 0
    for rel, dest in RSIS.items():
        if only is None or dest in only:
            convert_dmi(root / "icons" / rel, rel, Path(dest))
            count += 1
    for rel, dest in RSIS_V1.items():
        if only is None or dest in only:
            convert_dmi(Path(args.ds13_v1) / "icons" / rel, rel, Path(dest), SOURCE_URL_V1, "DS13-1.0", "icons/")
            count += 1
    if only is None:
        make_wall_growth()
        port_audio(root)
        print(f"{count} RSIs, audio in {AUDIO.relative_to(REPO)}")
    else:
        print(f"{count} RSIs")


if __name__ == "__main__":
    main()
