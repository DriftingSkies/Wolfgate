"""Tests for copy_assets.py. Run from this folder: python -m unittest test_copy_assets -v"""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import copy_assets as ca  # noqa: E402


def make_rsi(path: Path, states: list[str], copyright_: str = "by someone", crlf: bool = True) -> None:
    path.mkdir(parents=True)
    meta = {"version": 1, "license": "CC-BY-SA-3.0", "copyright": copyright_, "size": {"x": 32, "y": 32},
            "states": [{"name": s} for s in states]}
    text = json.dumps(meta, indent=4)
    (path / "meta.json").write_bytes((text.replace("\n", "\r\n") if crlf else text).encode("utf-8"))
    for s in states:
        (path / f"{s}.png").write_bytes(b"png-" + s.encode())


class RsiTest(unittest.TestCase):
    def test_copy_is_byte_for_byte_and_drops_stray_pngs(self):
        with tempfile.TemporaryDirectory() as tmp:
            src, dest = Path(tmp) / "a.rsi", Path(tmp) / "out/a.rsi"
            make_rsi(src, ["one", "two"])
            (src / "stray.png").write_bytes(b"x")
            written, problems = ca.copy_rsi(src, dest, None)
            self.assertEqual((dest / "meta.json").read_bytes(), (src / "meta.json").read_bytes())
            self.assertFalse((dest / "stray.png").exists())
            self.assertTrue((dest / "one.png").exists())
            self.assertEqual(written, 3)
            self.assertTrue(any("stray.png" in p for p in problems))
            self.assertEqual(ca.copy_rsi(src, dest, None)[0], 0)

    def test_trim_keeps_licence_and_line_endings(self):
        with tempfile.TemporaryDirectory() as tmp:
            src, dest = Path(tmp) / "a.rsi", Path(tmp) / "out/a.rsi"
            make_rsi(src, ["one", "two", "three"], copyright_="Taken from cmss13")
            _, problems = ca.copy_rsi(src, dest, ["two"])
            meta = json.loads((dest / "meta.json").read_text(encoding="utf-8"))
            self.assertEqual([s["name"] for s in meta["states"]], ["two"])
            self.assertEqual((meta["license"], meta["copyright"]), ("CC-BY-SA-3.0", "Taken from cmss13"))
            self.assertIn(b"\r\n", (dest / "meta.json").read_bytes())
            self.assertFalse((dest / "one.png").exists())
            self.assertEqual(problems, [])

    def test_trim_to_a_missing_state_is_a_problem(self):
        with tempfile.TemporaryDirectory() as tmp:
            src = Path(tmp) / "a.rsi"
            make_rsi(src, ["one"])
            _, problems = ca.copy_rsi(src, Path(tmp) / "out", ["nope"])
            self.assertTrue(any("nope" in p for p in problems))

    def test_merge_adds_states_and_extends_the_copyright(self):
        with tempfile.TemporaryDirectory() as tmp:
            rmc, cmu, dest = Path(tmp) / "rmc.rsi", Path(tmp) / "cmu.rsi", Path(tmp) / "out.rsi"
            make_rsi(rmc, ["base"], copyright_="Taken from cmss13")
            make_rsi(cmu, ["base", "extra"], copyright_="Taken from cmss13. extra by another")
            ca.copy_rsi(rmc, dest, None)
            written, problems = ca.merge_states(cmu, dest, ["extra"])
            meta = json.loads((dest / "meta.json").read_text(encoding="utf-8"))
            self.assertEqual([s["name"] for s in meta["states"]], ["base", "extra"])
            self.assertEqual(meta["copyright"], "Taken from cmss13. extra by another")
            self.assertEqual((dest / "base.png").read_bytes(), b"png-base")
            self.assertTrue((dest / "extra.png").exists())
            self.assertEqual(problems, [])
            self.assertEqual(ca.merge_states(cmu, dest, ["extra"])[0], 0)

    def test_merge_refuses_a_different_frame_size(self):
        with tempfile.TemporaryDirectory() as tmp:
            a, b, dest = Path(tmp) / "a.rsi", Path(tmp) / "b.rsi", Path(tmp) / "out.rsi"
            make_rsi(a, ["one"])
            make_rsi(b, ["one", "two"])
            meta = json.loads((b / "meta.json").read_text(encoding="utf-8"))
            meta["size"] = {"x": 64, "y": 64}
            (b / "meta.json").write_text(json.dumps(meta), encoding="utf-8")
            ca.copy_rsi(a, dest, None)
            self.assertTrue(ca.merge_states(b, dest, ["two"])[1])


class AttributionTest(unittest.TestCase):
    def test_renders_one_file_and_many_files_and_flags_uncertain_origin(self):
        text = ca.render_attributions([
            {"files": ["a.ogg"], "license": "CC0-1.0", "copyright": "me", "source": "http://x"},
            {"files": ["b.ogg", "c.ogg"], "license": "CC-BY-SA-3.0", "copyright": "long\n  text", "source": "http://y", "uncertain": True},
        ])
        self.assertIn('- files: ["a.ogg"]\n  license: "CC0-1.0"', text)
        self.assertIn('# Origin not confirmed; see the copyright line.\n- files:\n  - "b.ogg"\n  - "c.ogg"\n', text)
        self.assertIn('copyright: "long text"', text)

    def test_finds_the_nearest_entry(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "Resources"
            (root / "Audio/Sub").mkdir(parents=True)
            (root / "Audio/attributions.yml").write_text(
                '- files: ["Sub/x.ogg"]\n  license: "CC0-1.0"\n  copyright: "c"\n  source: "s"\n', encoding="utf-8")
            (root / "Audio/Sub/x.ogg").write_bytes(b"")
            found = ca.find_source_attribution(root / "Audio/Sub/x.ogg", root)
            self.assertEqual(found["license"], "CC0-1.0")
            (root / "Audio/Sub/y.ogg").write_bytes(b"")
            self.assertIsNone(ca.find_source_attribution(root / "Audio/Sub/y.ogg", root))


class PathTest(unittest.TestCase):
    def test_sprite_paths(self):
        self.assertEqual(ca.sprite_path("Resources/Textures/_WF/Vehicles/apc.rsi"), "_WF/Vehicles/apc.rsi")
        self.assertEqual(ca.sprite_path("Content.CMU/Resources/Textures/CMU14/Structures/vehicles/x.rsi"), "CMU14/Structures/vehicles/x.rsi")
        self.assertEqual(ca.sprite_path("Resources/Audio/_WF/Vehicles/a.ogg"), "/Audio/_WF/Vehicles/a.ogg")
        self.assertIsNone(ca.sprite_path("Resources/Prototypes/x.yml"))

    def test_the_shipped_config_maps_rmc_and_cmu_sprites(self):
        paths = ca.path_map(ca.load_config(), ca.load_sources())
        self.assertEqual(paths["_RMC14/Structures/Vehicles/apc.rsi"], "_WF/Vehicles/apc.rsi")
        self.assertEqual(paths["CMU14/Structures/vehicles/fv150/exterior.rsi"], "_WF/Vehicles/CMU/fv150/exterior.rsi")
        self.assertEqual(paths["/Audio/_RMC14/Vehicle/tank_driving.ogg"], "/Audio/_WF/Vehicles/tank_driving.ogg")
        self.assertNotIn("/Audio/_RMC14/Vehicle/weapons/flamethrower.ogg", paths)

    def test_the_table_says_which_merged_states_have_no_named_author(self):
        rows = ca.table_rows(ca.load_config(), ca.load_sources())
        tank = next(r for r in rows if "| `Resources/Textures/_WF/Vehicles/tank.rsi` |" in r)
        hardpoint = next(r for r in rows if "| `Resources/Textures/_WF/Vehicles/hardpoints/tank.rsi` |" in r)
        self.assertIn("CMU's copyright names no author for mountedflag_0, mountedflag_1, re700cupola_0.", tank)
        self.assertNotIn("no author for aev_base", tank)
        self.assertIn("CMU's copyright names no author for mounted_flag, re700_cupola.", hardpoint)


if __name__ == "__main__":
    unittest.main()
