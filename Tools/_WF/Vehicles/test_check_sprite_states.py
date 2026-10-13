"""Tests for check_sprite_states.py: a missing state, RSI or sound is reported, a child finds its parent's sprite, and the
repository's own vehicle prototypes are clean."""
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_sprite_states as tool  # noqa: E402

REPO = Path(__file__).resolve().parents[3]


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


class CheckSpriteStatesTest(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.root = Path(self._dir.name)
        write(self.root / "Resources/Textures/_WF/Vehicles/gun.rsi/meta.json", json.dumps({"states": [{"name": "barrel"}]}))
        write(self.root / "Resources/Audio/shot.ogg", "")

    def tearDown(self):
        self._dir.cleanup()

    def problems(self, yaml_text):
        path = self.root / "Resources/Prototypes/_WF/Vehicles/test.yml"
        write(path, yaml_text)
        return tool.Checker(str(self.root)).check_file(str(path))

    def test_a_known_state_and_sound_pass(self):
        found = self.problems("""
- type: entity
  id: Gun
  components:
  - type: Sprite
    sprite: _WF/Vehicles/gun.rsi
    state: barrel
  - type: Gun
    soundGunshot: /Audio/shot.ogg
""")
        self.assertEqual(found, [])

    def test_a_missing_state_rsi_and_sound_are_reported(self):
        found = self.problems("""
- type: entity
  id: Gun
  components:
  - type: Sprite
    sprite: _WF/Vehicles/gun.rsi
    state: muzzle
  - type: Gun
    soundGunshot: /Audio/missing.ogg
- type: entity
  id: Other
  components:
  - type: Sprite
    sprite: _WF/Vehicles/none.rsi
    state: barrel
""")
        self.assertEqual(len(found), 3, found)
        self.assertTrue(any("no state muzzle" in p for p in found))
        self.assertTrue(any("no sound /Audio/missing.ogg" in p for p in found))
        self.assertTrue(any("no RSI _WF/Vehicles/none.rsi" in p for p in found))

    def test_a_child_state_is_looked_up_in_the_parents_rsi(self):
        found = self.problems("""
- type: entity
  id: Base
  abstract: true
  components:
  - type: Sprite
    sprite: _WF/Vehicles/gun.rsi
- type: entity
  parent: Base
  id: Good
  components:
  - type: Sprite
    state: barrel
- type: entity
  parent: Base
  id: Bad
  components:
  - type: Sprite
    state: stock
""")
        self.assertEqual(found, ["Bad: no state stock in _WF/Vehicles/gun.rsi"])

    def test_a_part_state_may_live_in_the_nested_rsi_of_a_vehicle(self):
        write(self.root / "Resources/Textures/_WF/Vehicles/CMU/hull.rsi/meta.json", json.dumps({"states": [{"name": "plow_0"}]}))
        found = self.problems("""
- type: entity
  id: Hull
  components:
  - type: RMCVehicle
  - type: Sprite
    sprite: _WF/Vehicles/CMU/hull.rsi
    state: plow_0
- type: entity
  id: Plow
  components:
  - type: HardpointVisual
    vehicleState: plow_0
    damagedVehicleState: plow_1
""")
        self.assertEqual(found, ["Plow: damagedVehicleState plow_1 is in no vehicle RSI"])

    def test_the_vehicle_prototypes_of_the_repository_are_clean(self):
        if not (REPO / "Resources/Prototypes/_WF/Vehicles").exists():
            self.skipTest("not run from a repository checkout")

        checker = tool.Checker(str(REPO))
        problems = []
        for path in sorted((REPO / "Resources/Prototypes/_WF/Vehicles").rglob("*.yml")):
            problems += checker.check_file(str(path))

        self.assertEqual(problems, [])


if __name__ == "__main__":
    unittest.main()
