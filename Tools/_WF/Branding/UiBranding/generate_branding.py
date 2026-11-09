"""Generates the window branding textures from the Wolfgate logo.

Run from the repository root:  python Tools/_WF/Branding/UiBranding/generate_branding.py
Requires Pillow. Reads Resources/Textures/_WF/Branding/Logo/logo.png and writes the full logo (lettering and bars),
trimmed and pre-shrunk, into Resources/Textures/_WF/Branding/UiBranding: branding_header.png for title bars and
branding_footer.png for window footers. Both are drawn at half their pixel height, so they stay sharp at 200% UI.
"""
import os
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", ".."))
LOGO = os.path.join(ROOT, "Resources", "Textures", "_WF", "Branding", "Logo", "logo.png")
OUT = os.path.join(ROOT, "Resources", "Textures", "_WF", "Branding", "UiBranding")

SIZES = {
    "branding_header.png": 28,
    "branding_footer.png": 18,
}


def main():
    logo = Image.open(LOGO).convert("RGBA")
    logo = logo.crop(logo.getchannel("A").getbbox())
    os.makedirs(OUT, exist_ok=True)
    for name, height in SIZES.items():
        width = round(logo.width * height / logo.height)
        path = os.path.join(OUT, name)
        logo.resize((width, height), Image.LANCZOS).save(path, optimize=True)
        with open(path + ".yml", "w", newline="\n") as meta:
            meta.write("sample:\n  filter: true\n")
        print("wrote", os.path.relpath(path, ROOT), width, "x", height)


if __name__ == "__main__":
    main()
