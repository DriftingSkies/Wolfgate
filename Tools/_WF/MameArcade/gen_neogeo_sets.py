"""Generates Content.Client/_WF/MameArcade/NeoGeo/NeoGeoSets.cs from MAME's Neo Geo driver.

Usage: python Tools/_WF/MameArcade/gen_neogeo_sets.py <mame>/src/mame/snk/neogeo.cpp <MAME.NET mame.xml>

Lists the Neo Geo games MAME.NET emulates whose cartridges hold plain data, with the chip layout MAME loads them
in. Encrypted cartridges, and boards with controls the arcade has no keys for, are left out.
"""
import re
import sys
from pathlib import Path

OUT = Path(__file__).resolve().parents[3] / "Content.Client/_WF/MameArcade/NeoGeo/NeoGeoSets.cs"

# Cartridge types whose ROMs are used as dumped; MAME.NET emulates the protection chips of the last two.
PLAIN_CONFIGS = {"neobase", "fatfur2", "mslugx"}

REGIONS = {
    "cslot1:maincpu": "MainCpu",
    "cslot1:fixed": "Fixed",
    "cslot1:ymsnd:adpcma": "Ymsnd",
    "cslot1:ymsnd:adpcmb": "YmsndDeltaT",
    "cslot1:sprites": "Sprites",
}

# Regions MAME has that MAME.NET doesn't read.
IGNORED_REGIONS = {"mcu"}

LOAD_MODES = {
    "ROM_LOAD": "Bytes",
    "ROM_LOAD16_WORD_SWAP": "WordSwap",
    "ROM_LOAD16_BYTE": "Skip1",
    "ROM_LOAD32_BYTE": "Skip3",
}

AUDIO_SIZES = {"64K": 0x10000, "128K": 0x20000, "256K": 0x40000, "512K": 0x80000}
SFIX_SIZES = {"64K": 0x10000, "128K": 0x20000}


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def split_args(text):
    args, depth, cur, quoted = [], 0, "", False
    for c in text:
        if c == '"':
            quoted = not quoted
        if not quoted:
            if c == "(":
                depth += 1
            elif c == ")":
                depth -= 1
            elif c == "," and depth == 0:
                args.append(cur.strip())
                cur = ""
                continue
        cur += c
    if cur.strip():
        args.append(cur.strip())
    return args


def number(text):
    return int(text, 0)


def crc_of(hash_text):
    m = re.search(r"CRC\(\s*([0-9a-fA-F]{8})\s*\)", hash_text)
    return int(m.group(1), 16) if m else None


class Unsupported(Exception):
    pass


def parse_set(body):
    """Returns {region kind: (size, [load])}, where a load is (name, crc, file offset, length, region offset, mode)."""
    regions = {}
    current = None
    last = None  # [name, crc, bytes read so far, mode] of the chip a ROM_CONTINUE extends
    for m in re.finditer(r"\b([A-Z][A-Z0-9_]*)\s*\(((?:[^()]|\([^()]*\))*)\)", body):
        macro, args = m.group(1), split_args(m.group(2))
        if macro in ("CRC", "SHA1"):
            continue
        if macro in ("ROM_REGION", "ROM_REGION16_BE"):
            tag = args[1].strip('"')
            if tag in IGNORED_REGIONS:
                current = None
                continue
            if tag not in REGIONS:
                raise Unsupported("region " + tag)
            current = REGIONS[tag]
            regions[current] = (number(args[0]), [])
            last = None
        elif macro in LOAD_MODES:
            if current is None:
                continue
            name, offset, length, hashes = args[0].strip('"'), number(args[1]), number(args[2]), args[3]
            if "NO_DUMP" in hashes:
                raise Unsupported("undumped chip " + name)
            crc = crc_of(hashes)
            regions[current][1].append((name, crc, 0, length, offset, LOAD_MODES[macro]))
            last = [name, crc, length, LOAD_MODES[macro]]
        elif macro == "ROM_CONTINUE":
            if current is None:
                continue
            name, crc, consumed, mode = last
            offset, length = number(args[0]), number(args[1])
            regions[current][1].append((name, crc, consumed, length, offset, mode))
            last[2] = consumed + length
        elif macro == "ROM_RELOAD":
            if current is None:
                continue
            name, crc, _, mode = last
            regions[current][1].append((name, crc, 0, number(args[1]), number(args[0]), mode))
        elif macro == "ROM_COPY":
            source = REGIONS.get(args[0].strip('"'))
            if source is None:
                raise Unsupported("copy from " + args[0])
            regions[current][1].append(("@" + source, None, number(args[1]), number(args[3]), number(args[2]), "Copy"))
        elif macro.startswith("NEO_SFIX_"):
            size = SFIX_SIZES[macro.rsplit("_", 1)[1]]
            regions["Fixed"] = (0x20000, [(args[0].strip('"'), crc_of(args[1]), 0, size, 0, "Bytes")])
        elif macro.startswith("NEO_BIOS_AUDIO_"):
            kind = macro[len("NEO_BIOS_AUDIO_"):]
            if kind not in AUDIO_SIZES:
                raise Unsupported(macro)
            size = AUDIO_SIZES[kind]
            # MAME.NET banks into the bare M1 chip rather than MAME's mirrored region.
            regions["AudioCpu"] = (size, [(args[0].strip('"'), crc_of(args[1]), 0, size, 0, "Bytes")])
        elif macro in ("ROM_DEFAULT_BIOS", "ROM_SYSTEM_BIOS", "ROM_LOAD16_WORD_SWAP_BIOS", "ROM_FILL", "ROMX_LOAD",
                       "ROM_IGNORE", "ROM_LOAD_SWAP", "ROM_LOAD16_WORD"):
            raise Unsupported(macro)
    for kind in ("MainCpu", "Fixed", "AudioCpu", "Ymsnd", "Sprites"):
        if kind not in regions:
            raise Unsupported("no " + kind)
    return regions


def cs_string(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main():
    src = strip_comments(Path(sys.argv[1]).read_text(encoding="utf-8", errors="replace"))
    xml = re.sub(r"<!--.*?-->", "", Path(sys.argv[2]).read_text(encoding="utf-8"), flags=re.S)
    supported = {m.group(1) for m in re.finditer(r'<game name="([^"]+)" board="Neo Geo"', xml)}

    blocks = {m.group(1): m.group(2) for m in re.finditer(r"ROM_START\(\s*(\w+)\s*\)(.*?)ROM_END", src, re.S)}
    games = []
    for m in re.finditer(r"^GAME\((.*)\)\s*$", src, re.M):
        args = split_args(m.group(1))
        year, name, parent, config = args[0], args[1], args[2], args[3]
        manufacturer, title = args[8].strip('"'), args[9].strip('"')
        if name not in supported or config not in PLAIN_CONFIGS or name not in blocks:
            continue
        if "MACHINE_NOT_WORKING" in args[10]:
            print(f"skip {name}: not working in MAME")
            continue
        try:
            regions = parse_set(blocks[name])
        except Unsupported as e:
            print(f"skip {name}: {e}")
            continue
        games.append((name, "" if parent == "neogeo" else parent, title, year, manufacturer, regions))

    names = {g[0] for g in games}
    lines = [
        "// <auto-generated>",
        "// Generated by Tools/_WF/MameArcade/gen_neogeo_sets.py from MAME's src/mame/snk/neogeo.cpp",
        "// (BSD-3-Clause, copyright-holders: Bryan McPhail, Ernesto Corvi, Andrew Prime, Zsolt Vasvari).",
        "// Don't edit by hand; rerun the script.",
        "// </auto-generated>",
        "",
        "namespace Content.Client._WF.MameArcade.NeoGeo;",
        "",
        "public static partial class NeoGeoSets",
        "{",
        "    private static NeoGeoSet[] Build() => new NeoGeoSet[]",
        "    {",
    ]
    for name, parent, title, year, manufacturer, regions in sorted(games):
        if parent and parent not in names:
            parent = ""
        lines.append(f"        new({cs_string(name)}, {cs_string(parent)}, {cs_string(title)}, {cs_string(year)}, {cs_string(manufacturer)}, new NeoGeoRegion[]")
        lines.append("        {")
        for kind in ("MainCpu", "Fixed", "AudioCpu", "Ymsnd", "YmsndDeltaT", "Sprites"):
            if kind not in regions:
                continue
            size, loads = regions[kind]
            lines.append(f"            new(NeoGeoRegionKind.{kind}, 0x{size:x}, new NeoGeoLoad[]")
            lines.append("            {")
            for chip, crc, file_offset, length, offset, mode in loads:
                if mode == "Copy":
                    source = chip[1:]
                    lines.append(f"                NeoGeoLoad.Copy(NeoGeoRegionKind.{source}, 0x{file_offset:x}, 0x{length:x}, 0x{offset:x}),")
                    continue
                lines.append(f"                new({cs_string(chip)}, 0x{crc:08x}, 0x{file_offset:x}, 0x{length:x}, 0x{offset:x}, NeoGeoLoadMode.{mode}),")
            lines.append("            }),")
        lines.append("        }),")
    lines += ["    };", "}", ""]
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(lines), encoding="utf-8", newline="\n")
    print(f"wrote {len(games)} games to {OUT}")


if __name__ == "__main__":
    main()
