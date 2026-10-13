"""Finds the source checkouts the Vehicles tools read: RMC-14, Colonial Marines Universe and RMC-14's engine.

import_files.yml gives each source the name of its folder (`dir`), not a path on one machine. The path is, in order:

  1. the environment variable WOLFGATE_RMC_PATH, WOLFGATE_CMU_PATH or WOLFGATE_ENGINE_PATH,
  2. `dir` beside this checkout, or beside any folder above it (a worktree under .claude/worktrees finds the checkouts
     that sit next to the main one),
  3. `dir` beside this checkout, which may not exist; the tool then reports the missing file.

A --rmc, --cmu or --engine option on a tool wins over all of these.
"""
from __future__ import annotations

import os
from pathlib import Path

ENVIRONMENT = {"rmc": "WOLFGATE_RMC_PATH", "cmu": "WOLFGATE_CMU_PATH", "engine": "WOLFGATE_ENGINE_PATH"}


def find(key: str, entry: dict, repo: Path, environ: dict | None = None) -> str:
    """The path of one source: the environment variable, else `dir` beside the checkout or a folder above it."""
    environ = os.environ if environ is None else environ
    given = environ.get(ENVIRONMENT.get(key, ""), "")
    if given:
        return given
    for base in [repo.parent, *repo.parent.parents]:
        if (base / entry["dir"]).exists():
            return (base / entry["dir"]).as_posix()
    return (repo.parent / entry["dir"]).as_posix()


def resolve(sources: dict, repo: Path, environ: dict | None = None) -> dict:
    """The sources of import_files.yml with a `path` filled in on each, so the tools read `sources[key]["path"]`."""
    for key, entry in sources.items():
        entry["path"] = find(key, entry, repo, environ)
    return sources
