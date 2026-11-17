"""Builds the small WADs of sounds and music that every client ships, from the full Freedoom WADs.

Nearby players hear a game they may not have downloaded, so they need these lumps up front.

Usage: python Tools/_WG/DoomArcade/make_noise_pack.py
"""
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
GAMES = ("freedoom1", "freedoom2")


def build(game):
    source = ROOT / f"Resources/GameData/_WG/DoomArcade/{game}.wad"
    target = ROOT / f"Resources/Textures/_WG/DoomArcade/{game}-noise.wad"
    data = source.read_bytes()
    count, table = struct.unpack("<ii", data[4:12])

    lumps = []
    for i in range(count):
        position, size = struct.unpack("<ii", data[table + i * 16:table + i * 16 + 8])
        name = data[table + i * 16 + 8:table + i * 16 + 16]
        if name.startswith((b"DS", b"D_")):
            lumps.append((name, data[position:position + size]))

    body = bytearray()
    entries = bytearray()
    offset = 12
    for name, lump in lumps:
        entries += struct.pack("<ii", offset, len(lump)) + name
        body += lump
        offset += len(lump)

    target.write_bytes(b"PWAD" + struct.pack("<ii", len(lumps), offset) + body + entries)
    print(f"{target.name}: {len(lumps)} lumps, {target.stat().st_size} bytes")


if __name__ == "__main__":
    for game in GAMES:
        build(game)
