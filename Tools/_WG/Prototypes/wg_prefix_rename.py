#!/usr/bin/env python3
"""Renames the Wolfgate prefix from WF to WG: folders, file names and every text file.

For a branch cut before the rename. Copy this file out of main, run it from the repository root on a clean tree,
commit, then merge main. Entities and tiles the branch adds need their own old-id entries only if they were live.
  python wg_prefix_rename.py            rename everything
  python wg_prefix_rename.py --ids OUT   also write the old prototype ids, one "kind id" per line

Saved maps in a server's data folder are rewritten by the server itself (WGLegacyMapFilesSystem). To do it by hand,
with the server stopped and the folder backed up:
  python wg_prefix_rename.py --data FOLDER
"""
import os
import re
import subprocess
import sys

SKIP_PREFIXES = ("Resources/Changelog/", "RobustToolbox/", "Content.Server.Database/Migrations/")
BINARY_EXT = (
    ".png", ".ogg", ".wad", ".sf2", ".dll", ".ico", ".jpg", ".jpeg", ".svg", ".ttf", ".otf", ".wav", ".mid", ".zip",
    ".exe", ".pdn", ".xcf", ".webm", ".gif", ".aseprite", ".dmi", ".bmp", ".pdf", ".swf", ".woff", ".woff2",
)

RULES = [
    # _WF folders and namespaces, WFThing ids and types, a bare WF, WF_CONSTANTS
    (re.compile(rb"(?<![A-Za-z0-9])WF(?![a-z])"), b"WG"),
    # SharedWFThing, IWFThing, NET_WF_COMPANY
    (re.compile(rb"(?<=[a-z])WF(?=[A-Z][a-z])"), b"WG"),
    (re.compile(rb"(?<![A-Za-z0-9])IWF(?=[A-Z][a-z])"), b"IWG"),
    (re.compile(rb"(?<=[A-Z])_WF(?=_)"), b"_WG"),
    # WfThing, GetWfThing
    (re.compile(rb"(?<![A-Z0-9])Wf(?=[A-Z][a-z])"), b"Wg"),
    # wf-keys, wf.cvars, wf_files, wfKinds, _wfFields
    (re.compile(rb"(?<![A-Za-z0-9])wf(?=[-._A-Z])"), b"wg"),
    (re.compile(rb"(?<![A-Za-z0-9])wF(?=[A-Z][a-z])"), b"wG"),
    # a bare wf: locals, xmlns aliases, the [wf] config table, in_wf
    (re.compile(rb"(?<![A-Za-z0-9])wf(?![A-Za-z0-9_])"), b"wg"),
    # commands, RSI states and xmlns aliases
    (re.compile(rb"(?<![A-Za-z0-9])wf(?=(?:cavern|planet|lattice|hallucinate|crew|antenna|tabs)|[hgac](?![A-Za-z0-9]))"), b"wg"),
]
MARKERS = (b"WF", b"Wf", b"wf")
TILES_LINE = re.compile(rb"\s*tiles:\s")

TYPE_LINE = re.compile(rb"^- type:\s*([A-Za-z0-9_]+)")
ID_LINE = re.compile(rb"^  id: (WF[A-Za-z0-9_]+)\s*$")


def rename(data: bytes) -> bytes:
    if b"tiles:" in data:
        # A map chunk's tiles are base64, where the prefix can turn up by chance.
        return b"".join(line if TILES_LINE.match(line) else rename_text(line) for line in data.splitlines(True))
    return rename_text(data)


def rename_text(data: bytes) -> bytes:
    for rule, to in RULES:
        data = rule.sub(to, data)
    return data


def rename_data(folder: str) -> int:
    """Rewrites the saved .yml files of a server data folder in place."""
    changed = 0
    for root, _, names in os.walk(folder):
        for name in names:
            if not name.endswith(".yml"):
                continue

            path = os.path.join(root, name)
            with open(path, "rb") as handle:
                data = handle.read()
            if b"\0" in data[:8192] or not any(marker in data for marker in MARKERS):
                continue

            new = rename(data)
            if new != data:
                with open(path, "wb") as handle:
                    handle.write(new)
                changed += 1

    print(f"rewrote {changed} files under {folder}")
    return 0


def tracked() -> list[str]:
    out = subprocess.run(["git", "ls-files", "-z"], capture_output=True, check=True).stdout
    return [p.decode("utf-8") for p in out.split(b"\0") if p]


def is_text(path: str) -> bool:
    return not path.lower().endswith(BINARY_EXT)


def collect_ids(path: str, data: bytes, ids: list[tuple[str, str]]) -> None:
    """Prototype ids carrying the prefix, with their kind, from a prototype file."""
    if not path.startswith("Resources/Prototypes/") or not path.endswith(".yml"):
        return

    kind = None
    for line in data.splitlines():
        if match := TYPE_LINE.match(line):
            kind = match.group(1).decode()
        elif kind and (match := ID_LINE.match(line)):
            ids.append((kind, match.group(1).decode()))


def main() -> int:
    if "--data" in sys.argv:
        return rename_data(sys.argv[sys.argv.index("--data") + 1])

    ids_out = sys.argv[sys.argv.index("--ids") + 1] if "--ids" in sys.argv else None
    if not os.path.isdir("Content.Shared/_WF"):
        print("no Content.Shared/_WF here: this tree is already renamed")
        return 1

    files = [p for p in tracked() if not p.startswith(SKIP_PREFIXES)]
    ids: list[tuple[str, str]] = []
    changed = 0

    for path in files:
        if not is_text(path) or not os.path.isfile(path):
            continue

        with open(path, "rb") as handle:
            data = handle.read()
        if b"\0" in data[:8192] or not any(marker in data for marker in MARKERS):
            continue

        collect_ids(path, data, ids)
        new = rename(data)
        if path == "Resources/migration.yml":
            # Its keys are old saved ids, which keep their spelling.
            new = re.sub(rb"(?m)^WG([A-Za-z0-9_]+:)", rb"WF\1", new)
        if new != data:
            with open(path, "wb") as handle:
                handle.write(new)
            changed += 1

    # Whole folders move in one git mv each; what is left is single files.
    folders = sorted({p[: p.index("/_WF/") + 4] for p in files if "/_WF/" in p})
    for folder in folders:
        target = rename(folder.encode()).decode()
        subprocess.run(["git", "mv", folder, target], check=True)

    moved = 0
    for path in tracked():
        if path.startswith(SKIP_PREFIXES):
            continue
        target = rename(path.encode()).decode()
        if target == path:
            continue
        os.makedirs(os.path.dirname(target) or ".", exist_ok=True)
        subprocess.run(["git", "mv", path, target], check=True)
        moved += 1

    if ids_out:
        with open(ids_out, "w", encoding="utf-8", newline="\n") as handle:
            handle.writelines(f"{kind} {name}\n" for kind, name in sorted(set(ids)))

    print(f"rewrote {changed} files, moved {len(folders)} folders and {moved} files, found {len(ids)} prefixed ids")
    return 0


if __name__ == "__main__":
    sys.exit(main())
