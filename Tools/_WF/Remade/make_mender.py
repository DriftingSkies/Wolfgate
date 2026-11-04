"""Builds Resources/Textures/_WF/Remade/Mobs/mender.rsi from hunter.rsi.

The mender is the hunter's body bleached to a sick violet-grey, with a lying state for each limb so a severed one can lie on
the floor. Run from the repository root: python Tools/_WF/Remade/make_mender.py
"""
import json
import os
import shutil

from PIL import Image, ImageOps

SOURCE = "Resources/Textures/_WF/Remade/Mobs/hunter.rsi"
TARGET = "Resources/Textures/_WF/Remade/Mobs/mender.rsi"
DARK = (24, 16, 22)
PALE = (206, 194, 214)
KEEP = 0.3
LIMBS = {
    "l_leg_lying": "hunter_l_leg",
    "r_leg_lying": "hunter_r_leg",
    "l_arm_lying": "hunter_l_arm",
    "r_arm_lying": "hunter_r_arm",
    "head_lying": "hunter_head",
}


def tint(image):
    """Recolours an RGBA image between DARK and PALE by brightness, keeping a little of the original colour."""
    rgba = image.convert("RGBA")
    alpha = rgba.getchannel("A")
    grey = ImageOps.grayscale(rgba)
    coloured = ImageOps.colorize(grey, DARK, PALE).convert("RGBA")
    mixed = Image.blend(coloured, rgba, KEEP)
    mixed.putalpha(alpha)
    return mixed


def first_frame(image, size):
    """The first south-facing frame of a state image."""
    return image.crop((0, 0, size[0], size[1]))


def lying(frame, size):
    """A limb turned on its side and centred in a frame, scaled down if it no longer fits."""
    box = frame.getbbox()
    if box is None:
        return Image.new("RGBA", size)

    piece = frame.crop(box).rotate(90, expand=True)
    scale = min(1.0, size[0] / piece.width, size[1] / piece.height)
    if scale < 1.0:
        piece = piece.resize((max(1, int(piece.width * scale)), max(1, int(piece.height * scale))), Image.NEAREST)

    out = Image.new("RGBA", size)
    out.paste(piece, ((size[0] - piece.width) // 2, size[1] - piece.height - 6), piece)
    return out


def main():
    with open(os.path.join(SOURCE, "meta.json"), encoding="utf-8") as handle:
        meta = json.load(handle)

    if os.path.isdir(TARGET):
        shutil.rmtree(TARGET)
    os.makedirs(TARGET)

    size = (meta["size"]["x"], meta["size"]["y"])
    for state in meta["states"]:
        image = Image.open(os.path.join(SOURCE, state["name"] + ".png"))
        # The eyes glow on their own layer; leave them as they are.
        out = image if state["name"] == "eyes" else tint(image)
        out.save(os.path.join(TARGET, state["name"] + ".png"))

    for name, limb in LIMBS.items():
        tinted = tint(Image.open(os.path.join(SOURCE, limb + ".png")))
        lying(first_frame(tinted, size), size).save(os.path.join(TARGET, name + ".png"))
        meta["states"].append({"name": name})

    meta["copyright"] = meta.get("copyright", "") + " Recoloured, with lying limb states added, for Wolfgate's mender by Tools/_WF/Remade/make_mender.py."
    with open(os.path.join(TARGET, "meta.json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump(meta, handle, indent=2)
        handle.write("\n")


if __name__ == "__main__":
    main()
