#!/usr/bin/env python3
"""Checks the Vehicles module against the AGENTS.md rules a merge of many lanes tends to break.

    python Tools/_WF/Vehicles/check_conventions.py

Looks at the module's C#, XAML, prototypes, locale, tools and docs and reports, one finding per line:

  * a licence header in a file (AGENTS.md: no licence headers in _WF files),
  * `readonly` on a [Dependency] field,
  * a Fluent id defined twice in the locale, or a literal key given to Loc.GetString / {Loc ...} that no locale file defines,
  * TODO, HACK or FIXME, and a line comment that is code,
  * a doubled carriage return, or a file that mixes line endings.

The exit code is 1 when anything is found. `modules.py --check` covers files outside a module folder.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from csharp_scan import mask_code  # noqa: E402

REPO = Path(__file__).resolve().parents[3]

MODULE_FOLDERS = [
    "Content.Shared/_WF/Vehicles",
    "Content.Server/_WF/Vehicles",
    "Content.Client/_WF/Vehicles",
    "Content.IntegrationTests/Tests/_WF/Vehicles",
    "Content.Tests/_WF/Vehicles",
    "Resources/Prototypes/_WF/Vehicles",
    "Resources/Locale/en-US/_WF/Vehicles",
    "Tools/_WF/Vehicles",
    "Docs/_WF/Vehicles",
]
LOCALE_FOLDER = "Resources/Locale/en-US"
TEXT_SUFFIXES = {".cs", ".xaml", ".yml", ".ftl", ".py", ".md"}
CODE_SUFFIXES = {".cs", ".xaml"}

LICENCE_RE = re.compile(r"copyright\s*(?:\(c\)|©|\d{4})|licensed under|spdx-license|all rights reserved|permission is hereby granted", re.IGNORECASE)
DEPENDENCY_RE = re.compile(r"\[\s*Dependency\b[^\]]*\]")
TODO_RE = re.compile(r"\b(TODO|HACK|FIXME)\b")
FLUENT_ID_RE = re.compile(r"^(-?[A-Za-z][\w-]*)[ \t]*=", re.MULTILINE)
LOC_CALL_RE = re.compile(r"\bLoc\.(?:GetString|TryGetString)\(\s*\"([^\"{}]+)\"")
LOC_XAML_RE = re.compile(r"\{Loc\s+([A-Za-z][\w-]*)")
CODE_COMMENT_RE = re.compile(
    r"^\s*//\s*(?:(?:var|if|for|foreach|while|return|await|using|throw|public|private|protected|internal)\b.*|"
    r"[A-Za-z_][\w.<>\[\]]*(?:\([^)]*\)|\s*[-+*/]?=\s*.+))[;{}]\s*$"
)
SKIPPED_PARTS = {"__pycache__", "bin", "obj"}


def module_files(repo: Path, suffixes: set[str]) -> list[Path]:
    files = []
    for folder in MODULE_FOLDERS:
        base = repo / folder
        if base.is_dir():
            files += [p for p in base.rglob("*") if p.suffix in suffixes and not SKIPPED_PARTS & set(p.parts)]
    return sorted(files)


def locale_ids(repo: Path) -> dict[str, list[str]]:
    """Every Fluent id of the en-US locale with the files that define it."""
    ids: dict[str, list[str]] = {}
    for path in sorted((repo / LOCALE_FOLDER).rglob("*.ftl")):
        text = path.read_text(encoding="utf-8-sig")
        for name in FLUENT_ID_RE.findall(text):
            ids.setdefault(name, []).append(path.relative_to(repo).as_posix())
    return ids


def check(repo: Path) -> list[str]:
    findings: list[str] = []
    ids = locale_ids(repo)
    module_locale = (repo / "Resources/Locale/en-US/_WF/Vehicles").as_posix()

    for path in module_files(repo, TEXT_SUFFIXES):
        rel = path.relative_to(repo).as_posix()
        raw = path.read_bytes()
        text = raw.decode("utf-8-sig")
        lines = text.replace("\r\n", "\n").split("\n")

        if b"\r\r" in raw:
            findings.append(f"{rel}: doubled carriage return")
        if raw.count(b"\r\n") and raw.count(b"\n") != raw.count(b"\r\n"):
            findings.append(f"{rel}: mixed line endings")

        if path.suffix != ".md" and any(LICENCE_RE.search(line) for line in lines[:6]):
            findings.append(f"{rel}: licence header")

        if path.suffix != ".md" and path.name not in ("check_conventions.py", "test_check_conventions.py"):
            for number, line in enumerate(lines, 1):
                if TODO_RE.search(line):
                    findings.append(f"{rel}:{number}: {TODO_RE.search(line).group(1)}")

        if path.suffix == ".cs":
            masked = mask_code(text)
            for match in DEPENDENCY_RE.finditer(masked):
                end = masked.find(";", match.end())
                if end > 0 and re.search(r"\breadonly\b", masked[match.end():end]):
                    number = masked.count("\n", 0, match.start()) + 1
                    findings.append(f"{rel}:{number}: readonly [Dependency] field")
            for number, line in enumerate(lines, 1):
                if line.lstrip().startswith("///"):
                    continue
                if CODE_COMMENT_RE.match(line) and "upstream original" not in lines[max(number - 3, 0)]:
                    findings.append(f"{rel}:{number}: commented-out code")

        if path.suffix in CODE_SUFFIXES:
            used = set(LOC_CALL_RE.findall(text)) | (set(LOC_XAML_RE.findall(text)) if path.suffix == ".xaml" else set())
            for key in sorted(used):
                if key not in ids:
                    findings.append(f"{rel}: Loc key {key} is not defined in any locale file")

    for name, files in sorted(ids.items()):
        if any(f.startswith("Resources/Locale/en-US/_WF/Vehicles/") for f in files) and len(files) > 1:
            findings.append(f"Fluent id {name} is defined {len(files)} times: {', '.join(files)}")
    assert module_locale
    return findings


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--repo", type=Path, default=REPO, help="repository root (default: this checkout)")
    args = parser.parse_args(argv)
    findings = check(args.repo)
    for finding in findings:
        print(finding)
    print(f"{len(findings)} finding(s)")
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
