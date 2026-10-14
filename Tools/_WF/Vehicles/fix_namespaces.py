#!/usr/bin/env python3
"""Sets the namespace of every Vehicles C# and XAML file from its folder, then repairs the usings that named a moved type.

AGENTS.md: namespaces follow the folder. A file at Content.Shared/_WF/Vehicles/Core/X.cs declares
Content.Shared._WF.Vehicles.Core. Files that arrive in the old flat Content.<Side>._WF.Vehicles namespace (an import, a
merge from another lane) are moved by running this again; a second run changes nothing.

For every .cs and .xaml file under the module folders (shared, server, client, integration tests, unit tests) the script

  * sets `namespace X;` from the path, except for the files in PINNED, which extend an upstream type and keep its
    namespace (AGENTS.md allows that),
  * sets a XAML file's x:Class and the clr-namespace of its xmlns lines,
  * rewrites fully qualified references and `using` aliases and statics to the new namespaces,
  * adds the `using` a file now needs for a moved type it used before, and removes the module usings it no longer
    needs. A new using goes where the old module usings stood, or in sorted position when the file had none. A trailing
    `// WOLFGATE(...)` comment on the old line is copied to the new ones.

The same using repair runs on every other .cs and .xaml file of the repository that mentions a module namespace (the
marked hooks in SharedGunSystem.cs, the client GunSystem.cs and the like). Nothing else in those files is touched.
Line endings are kept: a CRLF working tree stays CRLF and no line ever ends in a doubled carriage return.

    python Tools/_WF/Vehicles/fix_namespaces.py            fix the tree
    python Tools/_WF/Vehicles/fix_namespaces.py --check    list what would change and exit 1 if anything would
    python Tools/_WF/Vehicles/fix_namespaces.py -v         also print each file it changes

Run it after any import or merge that adds Vehicles files, then build. The engine finds BUIs, components and YAML type
tags by type name and enum keys by short enum name, so a namespace move is safe for them. When the script cannot decide
(a file whose namespace is outside the module and not in PINNED, a partial type split across folders, a XAML file
that needs two namespaces for one prefix) it says so and exits 2.
"""
from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from csharp_scan import mask_code  # noqa: E402

REPO = Path(__file__).resolve().parents[3]

# Module folder (relative to the repository) -> namespace of the files directly in it.
ROOTS = {
    "Content.Shared/_WF/Vehicles": "Content.Shared._WF.Vehicles",
    "Content.Server/_WF/Vehicles": "Content.Server._WF.Vehicles",
    "Content.Client/_WF/Vehicles": "Content.Client._WF.Vehicles",
    "Content.IntegrationTests/Tests/_WF/Vehicles": "Content.IntegrationTests.Tests._WF.Vehicles",
    "Content.Tests/_WF/Vehicles": "Content.Tests._WF.Vehicles",
}

# Module files that extend an upstream type and keep its namespace: path -> (namespace, reason).
PINNED = {
    "Content.Client/_WF/Vehicles/Turret/GunSystem.AimOrigin.cs": (
        "Content.Client.Weapons.Ranged.Systems",
        "partial of the upstream client GunSystem",
    ),
}

CODE_SUFFIXES = {".cs", ".xaml"}
SKIP_DIRS = {"bin", "obj", "RobustToolbox", ".git", ".vs", "node_modules"}

NAMESPACE_RE = re.compile(r"^namespace[ \t]+([A-Za-z_][\w.]*)[ \t]*(;|\{)?", re.MULTILINE)
USING_RE = re.compile(r"^([ \t]*)using[ \t]+([A-Za-z_][\w.]*)[ \t]*;([^\r\n]*)$")
TYPE_RE = re.compile(
    r"^(?:(?:public|internal|static|sealed|abstract|partial|readonly|ref|unsafe|file|new)[ \t]+)*"
    r"(?:class|struct|interface|enum|record[ \t]+struct|record[ \t]+class|record)[ \t]+([A-Za-z_]\w*)",
    re.MULTILINE,
)
DELEGATE_RE = re.compile(
    r"^(?:(?:public|internal|unsafe|file|new)[ \t]+)*delegate[ \t]+[^\s(]+(?:<[^>]*>)?[ \t]+([A-Za-z_]\w*)", re.MULTILINE
)
EXTENSION_RE = re.compile(r"\b([A-Za-z_]\w*)[ \t]*(?:<[^<>()]*>)?[ \t]*\([ \t]*this[ \t]+")
WORD_RE = re.compile(r"[A-Za-z_]\w*")
XAML_CLASS_RE = re.compile(r'(x:Class=")([\w.]+)(")')
XAML_XMLNS_RE = re.compile(r'(xmlns:(\w+)="clr-namespace:)([\w.]+)((?:;assembly=[\w.]+)?")')


# ---------------------------------------------------------------------------------------------------------------
# Namespaces and using order
# ---------------------------------------------------------------------------------------------------------------

def target_namespace(rel: str) -> str | None:
    """The namespace a module file at `rel` (repository-relative, forward slashes) must declare, or None outside it."""
    if rel in PINNED:
        return PINNED[rel][0]
    for root, namespace in ROOTS.items():
        if rel.startswith(root + "/"):
            folders = rel[len(root) + 1:].split("/")[:-1]
            return ".".join([namespace, *folders])
    return None


def is_module_namespace(name: str) -> bool:
    """True for a module root namespace or anything below it."""
    return any(name == root or name.startswith(root + ".") for root in ROOTS.values())


def chain(namespace: str | None) -> set[str]:
    """The namespace and every namespace that encloses it: the ones whose types a file in it sees without a using."""
    if not namespace:
        return set()
    parts = namespace.split(".")
    return {".".join(parts[:i]) for i in range(1, len(parts) + 1)}


def using_key(name: str) -> tuple[int, str]:
    """Sort key of the repository's using order: System first, then by name with `_` before letters."""
    return (0 if name == "System" or name.startswith("System.") else 1, name.replace("_", " "))


# ---------------------------------------------------------------------------------------------------------------
# Files
# ---------------------------------------------------------------------------------------------------------------

@dataclass
class Decl:
    """A top-level type of a module file: where it is declared now and where its folder says it belongs."""
    name: str
    old: str
    new: str


@dataclass
class SourceFile:
    rel: str
    path: Path
    text: str
    bom: bool
    module: bool
    old_ns: str | None
    new_ns: str | None
    decls: list[str] = field(default_factory=list)
    extensions: list[str] = field(default_factory=list)


def split_lines(text: str) -> list[str]:
    """Lines with their terminators; only \\n ends a line, so a lone \\r or other separator never splits one."""
    return re.findall(r"[^\n]*\n|[^\n]+", text)


def line_ending(text: str) -> str:
    """The file's terminator: CRLF when most lines use it."""
    crlf = text.count("\r\n")
    return "\r\n" if crlf and crlf * 2 >= text.count("\n") else "\n"


def declared_namespace(text: str) -> str | None:
    found = NAMESPACE_RE.search(mask_code(text))
    return found.group(1) if found else None


def top_level_types(masked: str) -> list[str]:
    """Names of the types declared at column 0 (the module uses file-scoped namespaces)."""
    names = [m.group(1) for m in TYPE_RE.finditer(masked)]
    names += [m.group(1) for m in DELEGATE_RE.finditer(masked)]
    return names


def read_source(repo: Path, path: Path) -> SourceFile:
    rel = path.relative_to(repo).as_posix()
    raw = path.read_bytes()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    module = target_namespace(rel) is not None
    new_ns = target_namespace(rel)
    source = SourceFile(rel, path, text, bom, module, None, new_ns)
    if path.suffix == ".cs":
        masked = mask_code(text)
        found = NAMESPACE_RE.search(masked)
        source.old_ns = found.group(1) if found else None
        if found and found.group(2) == "{":
            raise SystemExit(f"{rel}: block-scoped namespaces are not supported; use `namespace X;`")
        source.decls = top_level_types(masked)
        source.extensions = sorted(set(EXTENSION_RE.findall(masked)))
    return source


def collect_module(repo: Path) -> list[SourceFile]:
    files = []
    for root in ROOTS:
        base = repo / root
        if not base.is_dir():
            continue
        for path in sorted(base.rglob("*")):
            if path.suffix in CODE_SUFFIXES and not (SKIP_DIRS & set(path.relative_to(base).parts)):
                files.append(read_source(repo, path))
    return files


def collect_outside(repo: Path, module_rels: set[str]) -> list[SourceFile]:
    """Every other .cs and .xaml file under a Content.* project that names a module namespace."""
    needles = [namespace.encode() for namespace in ROOTS.values()]
    files = []
    for project in sorted(repo.glob("Content.*")):
        if not project.is_dir():
            continue
        stack = [project]
        while stack:
            folder = stack.pop()
            for entry in folder.iterdir():
                if entry.is_dir():
                    if entry.name not in SKIP_DIRS:
                        stack.append(entry)
                elif entry.suffix in CODE_SUFFIXES:
                    rel = entry.relative_to(repo).as_posix()
                    if rel in module_rels:
                        continue
                    if any(needle in entry.read_bytes() for needle in needles):
                        files.append(read_source(repo, entry))
    return sorted(files, key=lambda f: f.rel)


# ---------------------------------------------------------------------------------------------------------------
# The type table
# ---------------------------------------------------------------------------------------------------------------

@dataclass
class Table:
    by_name: dict[str, list[Decl]]
    extension_owners: dict[str, list[Decl]]
    problems: list[str]


def build_table(module: list[SourceFile]) -> Table:
    """Every module type with its current and target namespace. Pinned files declare no module types."""
    by_name: dict[str, list[Decl]] = {}
    extension_owners: dict[str, list[Decl]] = {}
    problems: list[str] = []
    seen: dict[tuple[str, str], str] = {}
    for source in module:
        if source.path.suffix != ".cs" or source.rel in PINNED:
            continue
        if source.old_ns is None:
            problems.append(f"{source.rel}: no `namespace X;` line")
            continue
        if not is_module_namespace(source.old_ns):
            problems.append(
                f"{source.rel}: declares {source.old_ns}, which is outside the module; "
                "fix it, or add the file to PINNED with the reason"
            )
            continue
        assert source.new_ns is not None
        for name in source.decls:
            known = seen.get((source.old_ns, name))
            if known is not None:
                if known != source.new_ns:
                    problems.append(
                        f"{source.rel}: {source.old_ns}.{name} is split across folders; "
                        f"its other part belongs in {known} but this one in {source.new_ns}"
                    )
                continue
            seen[(source.old_ns, name)] = source.new_ns
            decl = Decl(name, source.old_ns, source.new_ns)
            by_name.setdefault(name, []).append(decl)
            for extension in source.extensions:
                extension_owners.setdefault(extension, []).append(decl)
    return Table(by_name, extension_owners, problems)


# ---------------------------------------------------------------------------------------------------------------
# Rewriting one file
# ---------------------------------------------------------------------------------------------------------------

def used_names(masked: str) -> set[str]:
    """Identifiers of the code that are not member accesses: `x.Name` does not use a type called Name."""
    names = set()
    for match in WORD_RE.finditer(masked):
        i = match.start() - 1
        while i >= 0 and masked[i].isspace():
            i -= 1
        if i >= 0 and masked[i] == "." and not (i > 0 and masked[i - 1] == "."):
            continue
        names.add(match.group(0))
    return names


def rewrite_qualified(text: str, table: Table) -> str:
    """Fully qualified references (including using aliases and statics) to a moved type."""
    for decls in table.by_name.values():
        for decl in decls:
            if decl.old != decl.new:
                text = re.sub(
                    r"(?<![\w.])" + re.escape(decl.old) + r"\." + re.escape(decl.name) + r"(?!\w)",
                    lambda _, d=decl: d.new + "." + d.name,
                    text,
                )
    return text


def needed_usings(source: SourceFile, masked: str, old_usings: set[str], table: Table, own_new: str | None) -> set[str]:
    """Module namespaces this file must import after the move for the types and extensions it used before.

    A file written against the old flat layout (it still declares a module root namespace, or imports one) saw every
    module type of that side through the root, including types already moved below it, so those count as visible."""
    old_visible = chain(source.old_ns) | old_usings
    stale = source.module and source.rel not in PINNED and source.old_ns != source.new_ns
    flat_roots = {r for r in ROOTS.values() if r in old_usings or (stale and r in old_visible)}

    def was_visible(decl: Decl) -> bool:
        return decl.old in old_visible or any(decl.old == r or decl.old.startswith(r + ".") for r in flat_roots)

    new_visible = chain(own_new)
    needed: set[str] = set()
    for name in used_names(masked):
        for candidate in (name, name + "Attribute"):
            for decl in table.by_name.get(candidate, ()):
                if was_visible(decl) and decl.new not in new_visible:
                    needed.add(decl.new)
    for method, owners in table.extension_owners.items():
        if re.search(r"\.\s*" + re.escape(method) + r"\b", masked):
            for decl in owners:
                if was_visible(decl) and decl.new not in new_visible:
                    needed.add(decl.new)
    return needed


def plain_usings(lines: list[str], namespace_line: int) -> list[tuple[int, re.Match]]:
    """The `using X;` lines before the namespace line, with their line index."""
    found = []
    for index in range(namespace_line):
        match = USING_RE.match(lines[index].rstrip("\r\n"))
        if match:
            found.append((index, match))
    return found


def rewrite_usings(lines: list[str], namespace_line: int, final: set[str], eol: str, in_module: bool) -> list[str]:
    """Replaces the module usings before the namespace line by exactly `final`; other usings are not touched.

    New usings take the place of the first module using that goes away (so a marked hook keeps its marker and stays
    inside the block the marker covers). With none going away they go after the module usings of a file outside the
    module, or in sorted position in a module file."""
    usings = plain_usings(lines, namespace_line)
    module_usings = [(i, m) for i, m in usings if is_module_namespace(m.group(2))]
    existing = [m.group(2) for _, m in module_usings]
    if set(existing) == final and len(set(existing)) == len(existing):
        return lines

    comment = next((m.group(3) for _, m in module_usings if "WOLFGATE" in m.group(3)), "")
    indent = module_usings[0][1].group(1) if module_usings else ""
    kept: set[str] = set()
    remove: set[int] = set()
    for index, match in module_usings:
        name = match.group(2)
        if name in final and name not in kept:
            kept.add(name)
        else:
            remove.add(index)
    additions = sorted(final - kept, key=using_key)

    def make(name: str) -> str:
        return f"{indent}using {name};{comment}{eol}"

    inserts: dict[int, list[str]] = {}
    if remove and additions:
        inserts[min(remove)] = [make(name) for name in additions]
    elif additions and module_usings and not in_module:
        inserts[module_usings[-1][0] + 1] = [make(name) for name in additions]
    elif additions:
        for name in additions:
            position = next((i for i, m in usings if using_key(m.group(2)) > using_key(name)), None)
            if position is None:
                position = usings[-1][0] + 1 if usings else namespace_line
            inserts.setdefault(position, []).append(make(name))
        if not usings:
            inserts[namespace_line].append(eol)

    out = []
    for index, line in enumerate(lines):
        out.extend(inserts.get(index, ()))
        if index not in remove:
            out.append(line)
    out.extend(inserts.get(len(lines), ()))
    return out


def rewrite_cs(source: SourceFile, table: Table) -> str:
    text = source.text
    eol = line_ending(text)
    masked = mask_code(text)
    namespace_match = NAMESPACE_RE.search(masked)
    own_new = source.new_ns if source.module else source.old_ns
    lines = split_lines(text)
    namespace_line = text.count("\n", 0, namespace_match.start()) if namespace_match else 0

    old_usings = {m.group(2) for _, m in plain_usings(lines, namespace_line)}
    code = "\n".join(masked.split("\n")[namespace_line + 1:]) if namespace_match else masked
    final = needed_usings(source, code, old_usings, table, own_new)
    if namespace_match:
        lines = rewrite_usings(lines, namespace_line, final, eol, source.module)
    text = "".join(lines)

    if source.module and source.old_ns and source.new_ns and source.old_ns != source.new_ns and source.rel not in PINNED:
        text = re.sub(
            r"^(namespace[ \t]+)" + re.escape(source.old_ns) + r"([ \t]*;)",
            lambda m: m.group(1) + source.new_ns + m.group(2),
            text,
            count=1,
            flags=re.MULTILINE,
        )
    return rewrite_qualified(text, table)


def rewrite_xaml(source: SourceFile, table: Table, problems: list[str]) -> str:
    text = source.text
    if source.module and source.new_ns:
        text = XAML_CLASS_RE.sub(
            lambda m: m.group(1) + source.new_ns + "." + m.group(2).rsplit(".", 1)[-1] + m.group(3), text
        )

    def xmlns(match: re.Match) -> str:
        prefix, namespace = match.group(2), match.group(3)
        if not is_module_namespace(namespace):
            return match.group(0)
        used = set(re.findall(r"(?<![\w:])" + re.escape(prefix) + r":([A-Za-z_]\w*)", text))
        targets = {d.new for name in used for d in table.by_name.get(name, ()) if d.old == namespace}
        if len(targets) > 1:
            problems.append(
                f"{source.rel}: xmlns:{prefix} uses types that now live in {', '.join(sorted(targets))}; "
                "give each namespace its own prefix"
            )
            return match.group(0)
        if len(targets) == 1:
            return match.group(1) + next(iter(targets)) + match.group(4)
        return match.group(0)

    text = XAML_XMLNS_RE.sub(xmlns, text)
    return rewrite_qualified(text, table)


def rewrite_file(source: SourceFile, table: Table, problems: list[str]) -> str:
    if source.path.suffix == ".xaml":
        return rewrite_xaml(source, table, problems)
    return rewrite_cs(source, table)


# ---------------------------------------------------------------------------------------------------------------
# Driver
# ---------------------------------------------------------------------------------------------------------------

def fix(repo: Path, write: bool = True, verbose: bool = False, out=print) -> tuple[list[str], list[str]]:
    """Fixes the tree under `repo`. Returns the repository-relative paths that changed and the problems found."""
    module = collect_module(repo)
    table = build_table(module)
    problems = list(table.problems)
    outside = collect_outside(repo, {source.rel for source in module})
    changed = []
    for source in [*module, *outside]:
        new_text = rewrite_file(source, table, problems)
        if new_text == source.text:
            continue
        if "\r\r" in new_text:
            problems.append(f"{source.rel}: the rewrite produced a doubled carriage return")
            continue
        changed.append(source.rel)
        if verbose:
            out(f"  {source.rel}")
        if write:
            data = new_text.encode("utf-8")
            source.path.write_bytes((b"\xef\xbb\xbf" if source.bom else b"") + data)
    return changed, problems


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--check", action="store_true", help="write nothing; exit 1 if any file would change")
    parser.add_argument("-v", "--verbose", action="store_true", help="print each file that changes")
    parser.add_argument("--repo", type=Path, default=REPO, help="repository root (default: this checkout)")
    args = parser.parse_args(argv)

    changed, problems = fix(args.repo, write=not args.check, verbose=args.verbose)
    for problem in problems:
        print(f"problem: {problem}", file=sys.stderr)
    verb = "would change" if args.check else "changed"
    print(f"{verb} {len(changed)} file(s)")
    if problems:
        return 2
    return 1 if args.check and changed else 0


if __name__ == "__main__":
    sys.exit(main())
