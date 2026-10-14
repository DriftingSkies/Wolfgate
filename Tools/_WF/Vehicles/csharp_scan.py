"""Masks the comments and string literals of C# source so the Vehicles tools can search the code alone."""
from __future__ import annotations


def _scan_char(text: str, i: int) -> int:
    """End of the character literal starting at i, or i when it is not one."""
    n = len(text)
    j = i + 1
    if j >= n:
        return i
    if text[j] == "\\":
        j += 2
        while j < n and text[j] != "'" and j - i < 12:
            j += 1
    else:
        j += 1
    return j + 1 if j < n and text[j] == "'" else i


def _skip_hole(text: str, i: int, braces: int) -> int:
    """End of an interpolation hole whose opening braces end just before i; nested strings and comments are skipped."""
    n = len(text)
    depth = 1
    while i < n and depth > 0:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if c in '"@$':
            j = _scan_string(text, i)
            if j > i:
                i = j
                continue
        if c == "'":
            j = _scan_char(text, i)
            if j > i:
                i = j
                continue
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
        i += 1
    return i


def _scan_string(text: str, i: int) -> int:
    """End of the string literal starting at i (prefixes included), or i when there is none."""
    n = len(text)
    j = i
    dollars = 0
    verbatim = False
    while j < n and text[j] in "@$" and j - i < 4:
        dollars += text[j] == "$"
        verbatim |= text[j] == "@"
        j += 1
    if j >= n or text[j] != '"':
        return i
    quotes = 0
    while j + quotes < n and text[j + quotes] == '"':
        quotes += 1
    raw = quotes >= 3
    interpolated = dollars > 0
    braces = max(dollars, 1)
    if raw:
        j += quotes
        closer = '"' * quotes
        while j < n:
            if text.startswith(closer, j):
                return j + quotes
            if interpolated and text.startswith("{" * braces, j) and not text.startswith("{" * (braces + 1), j):
                j = _skip_hole(text, j + braces, braces)
                continue
            j += 1
        return n
    j += 1
    while j < n:
        c = text[j]
        if verbatim:
            if c == '"':
                if j + 1 < n and text[j + 1] == '"':
                    j += 2
                    continue
                return j + 1
        else:
            if c == "\\":
                j += 2
                continue
            if c == '"':
                return j + 1
            if c == "\n":
                return j
        if interpolated and c == "{":
            if j + 1 < n and text[j + 1] == "{":
                j += 2
                continue
            j = _skip_hole(text, j + 1, 1)
            continue
        j += 1
    return n


def mask_code(text: str) -> str:
    """The text with comments and the insides of string and character literals blanked, same length and lines."""
    out = list(text)
    n = len(text)

    def blank(a: int, b: int) -> None:
        for k in range(a, b):
            if out[k] not in "\r\n":
                out[k] = " "

    i = 0
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            blank(i, j)
            i = j
            continue
        if c == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            blank(i, j)
            i = j
            continue
        if c in '"@$':
            j = _scan_string(text, i)
            if j > i:
                blank(i, j)
                i = j
                continue
        if c == "'":
            j = _scan_char(text, i)
            if j > i:
                blank(i, j)
                i = j
                continue
        i += 1
    return "".join(out)
