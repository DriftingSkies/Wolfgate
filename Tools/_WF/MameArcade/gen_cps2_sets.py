"""Generates Content.Client/_WF/MameArcade/Cps2/Cps2Sets.cs from MAME's CPS2 driver.

Usage: python Tools/_WF/MameArcade/gen_cps2_sets.py <mame>/src/mame/capcom/cps2.cpp <mame0150>/src/mame/machine/cps2crpt.c

Lists the standard CPS2 games for one or two players, with the chip layout MAME loads them in and the decryption
key: from the set's .key file when the player's zip has one, or else from the table older MAME versions kept, since
zips made before MAME 0.153 have no key file. Phoenix and bootleg boards, games for three or four players and games
with special controls are left out.
"""
import re
import sys
from pathlib import Path

OUT = Path(__file__).resolve().parents[3] / "Content.Client/_WF/MameArcade/Cps2/Cps2Sets.cs"

INPUTS = {"cps2_2p6b", "cps2_2p6bt", "cps2_2p3b", "cps2_2p2b", "cps2_2p1b", "cybots"}

REGIONS = {"maincpu": "MainCpu", "gfx": "Gfx", "audiocpu": "AudioCpu", "qsound": "QSound"}

CONSTANTS = {"CODE_SIZE": 0x400000, "QSOUND_SIZE": 0x50000}

LOAD_MODES = {
    "ROM_LOAD": "Bytes",
    "ROM_LOAD16_WORD_SWAP": "WordSwap",
    "ROM_LOAD16_BYTE": "Skip1",
    "ROM_LOAD64_WORD": "Word64",
    "ROM_LOAD64_BYTE": "Byte64",
}


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
    text = text.strip()
    return CONSTANTS[text] if text in CONSTANTS else int(text, 0)


def crc_of(hash_text):
    m = re.search(r"CRC\(\s*([0-9a-fA-F]{8})\s*\)", hash_text)
    return int(m.group(1), 16) if m else None


class Unsupported(Exception):
    pass


def bad_dumps(raw_block):
    """Older dumps MAME has commented out as BAD_DUMP, by (chip, region offset): many zips still carry them."""
    found = {}
    for m in re.finditer(r"//\s*ROM_LOAD\w*\(\s*\"([^\"]+)\"\s*,\s*(0x[0-9a-fA-F]+)\s*,[^\n]*BAD_DUMP[^\n]*CRC\(\s*([0-9a-fA-F]{8})", raw_block):
        found[(m.group(1), int(m.group(2), 16))] = int(m.group(3), 16)
    return found


def parse_set(raw_block):
    """Returns ({region: (size, [load])}, key chip). A load is (name, crc, file offset, length, offset, mode, alt crc)."""
    alternates = bad_dumps(raw_block)
    body = strip_comments(raw_block)
    regions = {}
    key = None
    current = None
    last = None
    for m in re.finditer(r"\b([A-Z][A-Z0-9_]*)\s*\(((?:[^()]|\([^()]*\))*)\)", body):
        macro, args = m.group(1), split_args(m.group(2))
        if macro in ("CRC", "SHA1"):
            continue
        if macro in ("ROM_REGION", "ROM_REGION16_BE"):
            tag = args[1].strip('"')
            if tag == "key":
                current = "Key"
                continue
            if tag not in REGIONS:
                raise Unsupported("region " + tag)
            current = REGIONS[tag]
            regions[current] = (number(args[0]), [])
            last = None
        elif macro in LOAD_MODES:
            name, offset, length, hashes = args[0].strip('"'), number(args[1]), number(args[2]), args[3]
            if "NO_DUMP" in hashes:
                raise Unsupported("undumped chip " + name)
            crc = crc_of(hashes)
            if current == "Key":
                key = (name, crc, length)
                continue
            mode = LOAD_MODES[macro]
            alt = alternates.get((name, offset), 0)
            regions[current][1].append((name, crc, 0, length, offset, mode, alt))
            last = [name, crc, length, mode, alt]
        elif macro == "ROM_CONTINUE":
            name, crc, consumed, mode, alt = last
            offset, length = number(args[0]), number(args[1])
            regions[current][1].append((name, crc, consumed, length, offset, mode, alt))
            last[2] = consumed + length
        elif macro == "ROM_FILL":
            offset, length, value = number(args[0]), number(args[1]), number(args[2])
            regions[current][1].append(("", value, 0, length, offset, "Fill", 0))
        else:
            raise Unsupported(macro)
    for kind in REGIONS.values():
        if kind not in regions:
            raise Unsupported("no " + kind)
    return regions, key


def old_keys(path):
    """The key table of MAME 0.150's cps2crpt.c: name -> (key0, key1, upper limit)."""
    keys = {}
    text = Path(path).read_text(encoding="utf-8", errors="replace")
    for m in re.finditer(r'\{\s*"(\w+)"\s*,\s*\{\s*(0x[0-9a-fA-F]+)\s*,\s*(0x[0-9a-fA-F]+)\s*\}\s*,?\s*(?:/\*[^*]*\*/)?\s*(0x[0-9a-fA-F]+)?\s*\}', text):
        name, k0, k1, upper = m.group(1), int(m.group(2), 16), int(m.group(3), 16), m.group(4)
        keys[name] = (k0, k1, int(upper, 16) if upper else 0x400000)
    return keys


def cs_string(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main():
    raw = Path(sys.argv[1]).read_text(encoding="utf-8", errors="replace")
    table = old_keys(sys.argv[2])
    blocks = {m.group(1): m.group(2) for m in re.finditer(r"ROM_START\(\s*(\w+)\s*\)(.*?)ROM_END", raw, re.S)}
    games = []
    for m in re.finditer(r"^GAME\((.*)\)\s*$", strip_comments(raw), re.M):
        args = split_args(m.group(1))
        year, name, parent, machine, inputs, init = args[0], args[1], args[2], args[3], args[4], args[6]
        manufacturer, title, flags = args[8].strip('"'), args[9].strip('"'), args[10]
        if machine != "cps2" or init != "init_cps2" or inputs not in INPUTS or name not in blocks:
            continue
        if "MACHINE_NOT_WORKING" in flags:
            print(f"skip {name}: not working in MAME")
            continue
        try:
            regions, key = parse_set(blocks[name])
        except Unsupported as e:
            print(f"skip {name}: {e}")
            continue
        if key is None and name not in table:
            print(f"skip {name}: no key")
            continue
        games.append((name, "" if parent == "0" else parent, title, year, manufacturer, regions, key, table.get(name)))

    names = {g[0] for g in games}
    lines = [
        "// <auto-generated>",
        "// Generated by Tools/_WF/MameArcade/gen_cps2_sets.py from MAME's src/mame/capcom/cps2.cpp (BSD-3-Clause,",
        "// copyright-holders: Paul Leaman, Andreas Naive, Nicola Salmoria, Charles MacDonald) and the key table of",
        "// MAME 0.150's src/mame/machine/cps2crpt.c. Don't edit by hand; rerun the script.",
        "// </auto-generated>",
        "",
        "using Content.Client._WF.MameArcade.NeoGeo;",
        "",
        "namespace Content.Client._WF.MameArcade.Cps2;",
        "",
        "public static partial class Cps2Sets",
        "{",
        "    private static Cps2Set[] Build() => new Cps2Set[]",
        "    {",
    ]
    for name, parent, title, year, manufacturer, regions, key, known in sorted(games):
        if parent and parent not in names:
            parent = ""
        key_chip = f"new MameChip({cs_string(key[0])}, 0x{key[1]:08x}, 0x{key[2]:x})" if key else "null"
        known_key = f"new Cps2Key(0x{known[0]:08x}, 0x{known[1]:08x}, 0x{known[2]:x})" if known else "null"
        lines.append(f"        new({cs_string(name)}, {cs_string(parent)}, {cs_string(title)}, {cs_string(year)}, {cs_string(manufacturer)},")
        lines.append(f"            {key_chip}, {known_key}, new Cps2Region[]")
        lines.append("        {")
        for kind in REGIONS.values():
            size, loads = regions[kind]
            lines.append(f"            new(Cps2RegionKind.{kind}, 0x{size:x}, new Cps2Load[]")
            lines.append("            {")
            for chip, crc, file_offset, length, offset, mode, alt in loads:
                alt_text = f", 0x{alt:08x}" if alt else ""
                lines.append(f"                new({cs_string(chip)}, 0x{crc:08x}, 0x{file_offset:x}, 0x{length:x}, 0x{offset:x}, Cps2LoadMode.{mode}{alt_text}),")
            lines.append("            }),")
        lines.append("        }),")
    lines += ["    };", "}", ""]
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(lines), encoding="utf-8", newline="\n")
    print(f"wrote {len(games)} games to {OUT}")


if __name__ == "__main__":
    main()
