"""Tests for source_paths.py. Run from this folder: python -m unittest test_source_paths -v"""
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import copy_assets  # noqa: E402
import import_rmc  # noqa: E402
import source_paths  # noqa: E402

SOURCES = {"rmc": {"dir": "RMC-14"}, "cmu": {"dir": "ColonialMarinesUniverse"}, "engine": {"dir": "RMC-14/RobustToolbox"}}


def fresh() -> dict:
    return {key: dict(entry) for key, entry in SOURCES.items()}


class FindTest(unittest.TestCase):
    def test_the_environment_variable_wins(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp) / "Wolfgate"
            (Path(tmp) / "RMC-14").mkdir()
            found = source_paths.resolve(fresh(), repo, {"WOLFGATE_RMC_PATH": "/elsewhere/rmc", "WOLFGATE_ENGINE_PATH": "/e"})
            self.assertEqual(found["rmc"]["path"], "/elsewhere/rmc")
            self.assertEqual(found["engine"]["path"], "/e")

    def test_a_checkout_beside_this_one_is_found(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp) / "Wolfgate"
            (Path(tmp) / "RMC-14/RobustToolbox").mkdir(parents=True)
            (Path(tmp) / "ColonialMarinesUniverse").mkdir()
            found = source_paths.resolve(fresh(), repo, {})
            self.assertEqual(found["rmc"]["path"], (Path(tmp) / "RMC-14").as_posix())
            self.assertEqual(found["cmu"]["path"], (Path(tmp) / "ColonialMarinesUniverse").as_posix())
            self.assertEqual(found["engine"]["path"], (Path(tmp) / "RMC-14/RobustToolbox").as_posix())

    def test_a_worktree_finds_the_checkouts_beside_the_main_one(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp) / "Wolfgate/.claude/worktrees/lane"
            (Path(tmp) / "ColonialMarinesUniverse").mkdir()
            found = source_paths.resolve(fresh(), repo, {})
            self.assertEqual(found["cmu"]["path"], (Path(tmp) / "ColonialMarinesUniverse").as_posix())

    def test_a_missing_checkout_still_gets_a_path_beside_this_one(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp) / "Wolfgate"
            found = source_paths.resolve(fresh(), repo, {})
            self.assertEqual(found["rmc"]["path"], (Path(tmp) / "RMC-14").as_posix())

    def test_an_empty_variable_is_ignored(self):
        with tempfile.TemporaryDirectory() as tmp:
            found = source_paths.resolve(fresh(), Path(tmp) / "Wolfgate", {"WOLFGATE_CMU_PATH": ""})
            self.assertEqual(found["cmu"]["path"], (Path(tmp) / "ColonialMarinesUniverse").as_posix())


class ToolsTest(unittest.TestCase):
    """Every tool reads its sources through source_paths, and none keeps a machine's path."""

    def test_the_shipped_config_names_folders_not_paths(self):
        raw = (Path(__file__).resolve().parent / "import_files.yml").read_text(encoding="utf-8")
        self.assertNotRegex(raw, r"(?m)^\s+path:")
        self.assertNotRegex(raw, r"[A-Za-z]:[/\\]Users")

    def test_the_tools_resolve_every_source(self):
        for sources in (import_rmc.load_config()["sources"], copy_assets.load_sources()):
            for key in ("rmc", "cmu", "engine"):
                self.assertTrue(sources[key]["path"], key)

    def test_the_variable_reaches_every_tool(self):
        import os
        old = os.environ.get("WOLFGATE_ENGINE_PATH")
        os.environ["WOLFGATE_ENGINE_PATH"] = "/some/engine"
        try:
            self.assertEqual(import_rmc.load_config()["sources"]["engine"]["path"], "/some/engine")
            self.assertEqual(copy_assets.load_sources()["engine"]["path"], "/some/engine")
        finally:
            if old is None:
                del os.environ["WOLFGATE_ENGINE_PATH"]
            else:
                os.environ["WOLFGATE_ENGINE_PATH"] = old


if __name__ == "__main__":
    unittest.main()
