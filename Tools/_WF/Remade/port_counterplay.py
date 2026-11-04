#!/usr/bin/env python3
"""Converts the plasma cutter's art and sound from DS13-1.0 into the Remade module.

    python Tools/_WF/Remade/port_counterplay.py [--ds13 C:/Users/jzo12/Documents/GitHub/DS13-1.0]

port_assets.py wipes Resources/Audio/_WF/Remade on a rerun, so run this after it. Reruns overwrite the output.
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / "Tools/_WF/Genitals"))
import dmi  # noqa: E402

TEXTURES = REPO / "Resources/Textures/_WF/Remade"
AUDIO = REPO / "Resources/Audio/_WF/Remade"
SOURCE_URL = "https://github.com/DS-13-Dev-Team/DS13"
LICENSE = "CC-BY-SA-3.0"


def save_rsi(dest: Path, copyright_text: str, states: dict[str, list[Image.Image]]) -> None:
    """states maps a state name to its directions (1 or 4 images, one frame each)."""
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)
    size = next(iter(states.values()))[0].size
    metas = []
    for name, dirs in states.items():
        sheet = Image.new("RGBA", (size[0] * len(dirs), size[1]))
        for i, img in enumerate(dirs):
            sheet.paste(img, (i * size[0], 0))
        sheet.save(dest / f"{name}.png")
        meta: dict = {"name": name}
        if len(dirs) > 1:
            meta["directions"] = len(dirs)
        metas.append(meta)
    doc = {"version": 1, "license": LICENSE, "copyright": copyright_text, "size": {"x": size[0], "y": size[1]}, "states": metas}
    (dest / "meta.json").write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8", newline="\n")


def state_image(path: Path, state: str) -> Image.Image:
    _, _, loaded = dmi.load_dmi(path)
    for st in loaded:
        if st["name"] == state:
            return st["img"][0][0]
    raise SystemExit(f"{path} has no state {state}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ds13", default="C:/Users/jzo12/Documents/GitHub/DS13-1.0")
    root = Path(parser.parse_args().ds13)

    icon = state_image(root / "icons/obj/tools.dmi", "plasmacutter")
    # DS13 has no hand or belt sprites for it, so those are the icon in all four directions.
    save_rsi(
        TEXTURES / "Objects/cutter.rsi",
        f"Taken from DS13-1.0 at {SOURCE_URL} (icons/obj/tools.dmi, plasmacutter); held and worn states copy the icon",
        {"icon": [icon], "inhand-left": [icon] * 4, "inhand-right": [icon] * 4, "equipped-BELT": [icon] * 4},
    )

    effects = root / "icons/effects/projectiles.dmi"
    save_rsi(
        TEXTURES / "Effects/cutter.rsi",
        f"Taken from DS13-1.0 at {SOURCE_URL} (icons/effects/projectiles.dmi)",
        {"muzzle": [state_image(effects, "muzzle_plasmacutter")], "impact": [state_image(effects, "impact_plasmacutter")]},
    )

    sound = AUDIO / "cutter/plasma_cutter.ogg"
    sound.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(root / "sound/weapons/plasma_cutter.ogg", sound)
    entry = (
        '- files: ["cutter/plasma_cutter.ogg"]\n'
        f'  license: "{LICENSE}"\n'
        '  copyright: "Taken from DS13-1.0"\n'
        f'  source: "{SOURCE_URL}"\n'
    )
    path = AUDIO / "attributions.yml"
    text = path.read_text(encoding="utf-8") if path.exists() else ""
    if "cutter/plasma_cutter.ogg" not in text:
        path.write_text(text.rstrip("\n") + "\n\n" + entry, encoding="utf-8", newline="\n")
    print("plasma cutter art and sound ported")


if __name__ == "__main__":
    main()
