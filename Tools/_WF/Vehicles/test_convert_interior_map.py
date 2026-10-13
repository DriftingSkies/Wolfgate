"""Tests for convert_interior_map.py. Run from this folder: python -m unittest test_convert_interior_map -v"""
import base64
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import convert_interior_map as conv  # noqa: E402

MAP = """meta:
  format: 7
  category: Map
  engineVersion: 264.0.2
  forkId: ""
  forkVersion: ""
  time: 03/18/2026 20:29:20
  entityCount: 9
maps:
- 1
grids:
- 2
orphans: []
nullspace: []
tilemap:
  1: Space
  2: CMFloorPlate
  0: RMCVoid
  3: Mystery
entities:
- proto: ""
  entities:
  - uid: 1
    components:
    - type: MetaData
      name: Map Entity
    - type: Transform
    - type: Map
      mapPaused: True
  - uid: 2
    components:
    - type: MetaData
      name: grid
    - type: Transform
      parent: 1
    - type: MapGrid
      chunks:
        0,0:
          ind: 0,0
          tiles: AAAA
          version: 7
    - type: AreaGrid
      labels: {}
      areas:
        0,0: Foo
    - type: GridAtmosphere
      version: 2
      data:
        chunkSize: 4
- proto: CMChairComfy
  entities:
  - uid: 3
    components:
    - type: Transform
      pos: 1,1
      parent: 2
- proto: RMCCrate
  entities:
  - uid: 4
    components:
    - type: Transform
      pos: 2,2
      parent: 2
    - type: ContainerContainer
      containers:
        entity_storage: !type:Container
          showEnts: False
          occludes: True
          ents:
          - 5
          - 3
    missingComponents:
    - Corrodible
- proto: RMCGadget
  entities:
  - uid: 5
    components:
    - type: Transform
      parent: 4
  - uid: 6
    components:
    - type: Transform
      pos: 3,3
      parent: 2
    - type: Weird
- proto: CMChairComfy
  entities:
  - uid: 7
    components:
    - type: Transform
      pos: 4,4
      parent: 2
- proto: Seat
  entities:
  - uid: 8
    components:
    - type: Transform
      pos: 5,5
      parent: 2
    - type: VehicleDriverSeat
      skills:
        all:
          RMCSkillVehicles: 1
      other: 3
    - type: RemoveComponents
      components:
      - type: Damageable
    missingComponents:
    - InteractionTransparency
    - Corrodible
- proto: Seat2
  entities:
  - uid: 9
    components:
    - type: Transform
      parent: 6
"""

MAPPING = {
    "prototypes": {"CMChairComfy": "ComfyChair", "RMCGadget": None, "Seat2": "Seat"},
    "components": {"AreaGrid": None, "GridAtmosphere": None, "RemoveComponents": None, "Weird": "Strange"},
    "component_fields": {"VehicleDriverSeat": ["skills"]},
    "tiles": {"CMFloorPlate": "FloorSteel"},
    "provided_by": [{"source": "rmc", "paths": ["protos"], "ids": ["Pending"], "component_roots": ["cs"]}],
    "known_engine_components": ["Transform", "MetaData", "Map", "MapGrid", "ContainerContainer"],
}


def make_knowledge(root: Path) -> conv.Knowledge:
    repo = root / "repo"
    (repo / "Resources/Prototypes").mkdir(parents=True)
    (repo / "Resources/Prototypes/a.yml").write_text(
        "- type: entity\n  id: ComfyChair\n\n- type: entity\n  parent: Base\n  id: Seat\n\n"
        "- type: tile\n  id: FloorSteel\n\n- type: tile\n  id: Space\n  name: x\n\n- type: tile\n  id: RMCVoid\n",
        encoding="utf-8")
    (repo / "Content.Shared").mkdir()
    (repo / "Content.Shared/Foo.cs").write_text(
        '[RegisterComponent, NetworkedComponent]\npublic sealed partial class StrangeComponent : Component\n{\n}\n'
        '[RegisterComponent]\n[ComponentProtoName("Seat")]\npublic sealed partial class VehicleDriverSeatComponent : Component\n{\n}\n',
        encoding="utf-8")
    rmc = root / "rmc"
    (rmc / "protos").mkdir(parents=True)
    (rmc / "protos/b.yml").write_text("- type: entity\n  id: RMCCrate\n", encoding="utf-8")
    (rmc / "cs").mkdir()
    (rmc / "cs/Pending.cs").write_text("[RegisterComponent]\npublic sealed partial class VehicleExitComponent : Component { }\n",
                                       encoding="utf-8")
    return conv.Knowledge(repo, MAPPING, {"rmc": {"path": str(rmc)}})


def run_conversion(text: str):
    with tempfile.TemporaryDirectory() as tmp:
        know = make_knowledge(Path(tmp))
        doc = conv.MapDoc(text)
        report = conv.Report()
        conv.convert(doc, MAPPING, know, report, "test.yml")
        return doc, report


class AtmosphereTest(unittest.TestCase):
    SPEC = {"volume": 2500, "temperature": 293.15, "gas_count": 4, "moles": [21.5, 82.0]}

    def convert(self, text):
        with tempfile.TemporaryDirectory() as tmp:
            know = make_knowledge(Path(tmp))
            doc = conv.MapDoc(text)
            report = conv.Report()
            conv.convert(doc, {**MAPPING, "map_atmosphere": self.SPEC}, know, report, "test.yml")
            return doc, report

    def test_map_entity_gets_an_immutable_mixture(self):
        doc, report = self.convert(MAP)
        text = doc.render()
        self.assertEqual(report.atmospheres, 1)
        self.assertIn("    - type: MapAtmosphere\n      space: False\n      mixture:\n        volume: 2500\n"
                      "        immutable: True\n        temperature: 293.15\n        moles:\n"
                      "        - 21.5\n        - 82.0\n        - 0\n        - 0\n", text)
        self.assertEqual(conv.verify(doc), [])

    def test_an_existing_atmosphere_is_left_alone(self):
        with_atmosphere = MAP.replace("      mapPaused: True\n", "      mapPaused: True\n    - type: MapAtmosphere\n      space: True\n", 1)
        doc, report = self.convert(with_atmosphere)
        self.assertEqual(report.atmospheres, 0)
        self.assertEqual(doc.render().count("MapAtmosphere"), 1)

    def test_no_atmosphere_without_the_mapping_key(self):
        doc, report = run_conversion(MAP)
        self.assertEqual(report.atmospheres, 0)
        self.assertNotIn("MapAtmosphere", doc.render())


class ParseTest(unittest.TestCase):
    def test_round_trip_is_exact_lf(self):
        self.assertEqual(conv.MapDoc(MAP).render(), MAP)

    def test_round_trip_is_exact_crlf(self):
        crlf = MAP.replace("\n", "\r\n")
        self.assertEqual(conv.MapDoc(crlf).render(), crlf)

    def test_keeps_a_closing_document_marker(self):
        text = MAP + "...\n"
        doc = conv.MapDoc(text)
        self.assertEqual(doc.render(), text)
        self.assertEqual(doc.entity_count(), 9)

    def test_doubled_carriage_returns_are_not_written(self):
        doc = conv.MapDoc(MAP.replace("\n", "\r\r\n"))
        self.assertNotIn("\r\r", doc.render())

    def test_reads_tilemap_and_groups(self):
        doc = conv.MapDoc(MAP)
        self.assertEqual(doc.tilemap(), {1: "Space", 2: "CMFloorPlate", 0: "RMCVoid", 3: "Mystery"})
        self.assertEqual(doc.groups[0].proto, "")
        self.assertEqual(doc.entity_count(), 9)


class ConvertTest(unittest.TestCase):
    def setUp(self):
        self.doc, self.report = run_conversion(MAP)
        self.text = self.doc.render()

    def test_deletes_mapped_to_null_and_children(self):
        uids = {e.uid for _, e in self.doc.entities()}
        self.assertNotIn(6, uids)
        self.assertNotIn(9, uids)  # parented to 6
        self.assertEqual(uids, {1, 2, 3, 4, 7, 8})
        self.assertEqual(self.report.removed["RMCGadget"], 2)
        self.assertEqual(self.report.removed["Seat (child of a deleted entity)"], 1)

    def test_removes_references_to_deleted_entities(self):
        self.assertIn("          ents:\n          - 3\n", self.text)
        self.assertNotIn("- 5\n", self.text)

    def test_empty_container_list_becomes_empty_list(self):
        text = MAP.replace("          - 3\n", "")
        doc, _ = run_conversion(text)
        self.assertIn("          ents: []", doc.render())

    def test_renames_and_merges_prototype_groups(self):
        protos = [g.proto for g in self.doc.groups]
        self.assertEqual(protos.count("ComfyChair"), 1)
        self.assertEqual(len(next(g for g in self.doc.groups if g.proto == "ComfyChair").entities), 2)
        self.assertNotIn("CMChairComfy", self.text)

    def test_entity_count_is_updated(self):
        self.assertIn("  entityCount: 6\n", self.text)
        self.assertEqual(conv.verify(self.doc), [])

    def test_drops_grid_blocks_and_registry(self):
        self.assertNotIn("AreaGrid", self.text)
        self.assertNotIn("GridAtmosphere", self.text)
        self.assertNotIn("RemoveComponents", self.text)
        self.assertNotIn("chunkSize", self.text)
        self.assertIn("    - type: MapGrid\n", self.text)

    def test_missing_components_entry_stays_unless_the_component_is_dropped(self):
        self.assertIn("    missingComponents:\n    - InteractionTransparency\n    - Corrodible\n", self.text)

    def test_drops_a_component_field_with_its_nested_lines(self):
        self.assertNotIn("skills", self.text)
        self.assertNotIn("RMCSkillVehicles", self.text)
        self.assertIn("      other: 3\n", self.text)

    def test_renames_tiles(self):
        tiles = self.doc.tilemap()
        self.assertEqual(tiles[2], "FloorSteel")
        self.assertEqual(tiles[0], "RMCVoid")

    def test_other_uids_are_not_renumbered(self):
        self.assertIn("  - uid: 7\n", self.text)
        self.assertIn("  - uid: 8\n", self.text)

    def test_untouched_lines_are_byte_for_byte(self):
        self.assertIn("    - type: Map\n      mapPaused: True\n", self.text)
        self.assertIn("          tiles: AAAA\n", self.text)

    def test_crlf_input_stays_crlf(self):
        doc, _ = run_conversion(MAP.replace("\n", "\r\n"))
        out = doc.render()
        self.assertEqual(out.count("\r\n"), out.count("\n"))
        self.assertNotIn("\r\r", out)


class ReportTest(unittest.TestCase):
    def test_classifies_unmapped_present_and_pending(self):
        text = MAP.replace("proto: Seat2\n", "proto: Pending\n").replace("proto: Seat\n", "proto: Nope\n").replace("Mystery", "Space")
        text = text.replace("      pos: 1,1\n      parent: 2\n", "      pos: 1,1\n      parent: 2\n    - type: VehicleExit\n    - type: Odd\n")
        doc, report = run_conversion(text)
        self.assertEqual(set(report.unmapped["prototype"]), {"Nope"})
        self.assertIn("RMCCrate", report.pending["prototype"])
        self.assertEqual(set(report.unmapped["component"]), {"Odd", "VehicleDriverSeat"})
        self.assertIn("VehicleExit", report.pending["component"])
        self.assertEqual(report.unmapped["tile"], {})

    def test_reports_a_mapping_target_that_does_not_exist(self):
        mapping = dict(MAPPING, prototypes={"CMChairComfy": "NoSuchChair"})
        with tempfile.TemporaryDirectory() as tmp:
            know = make_knowledge(Path(tmp))
            report = conv.Report()
            conv.convert(conv.MapDoc(MAP), mapping, know, report, "t.yml")
        self.assertIn("NoSuchChair", report.bad_targets)

    def test_tile_mapped_to_null_becomes_space(self):
        mapping = dict(MAPPING, tiles={"CMFloorPlate": None})
        with tempfile.TemporaryDirectory() as tmp:
            know = make_knowledge(Path(tmp))
            doc = conv.MapDoc(MAP)
            conv.convert(doc, mapping, know, conv.Report(), "t.yml")
        self.assertEqual(doc.tilemap()[2], "Space")


class MapPrototypesTest(unittest.TestCase):
    """`map_prototypes` renames a prototype in one output map only, and wins over `prototypes`."""

    def uids_by_proto(self, name: str) -> dict[str, set[int]]:
        mapping = dict(MAPPING, map_prototypes={"only_here.yml": {"Seat": "ComfyChair", "CMChairComfy": "Seat"}})
        with tempfile.TemporaryDirectory() as tmp:
            know = make_knowledge(Path(tmp))
            doc = conv.MapDoc(MAP)
            conv.convert(doc, mapping, know, conv.Report(), name)
        return {g.proto: {e.uid for e in g.entities} for g in doc.groups if g.proto}

    def test_applies_to_the_named_map(self):
        groups = self.uids_by_proto("only_here.yml")
        self.assertEqual(groups["Seat"], {3, 7})
        self.assertEqual(groups["ComfyChair"], {8})

    def test_leaves_other_maps_alone(self):
        groups = self.uids_by_proto("elsewhere.yml")
        self.assertEqual(groups["ComfyChair"], {3, 7})
        self.assertEqual(groups["Seat"], {8})


class VerifyTest(unittest.TestCase):
    def test_flags_dangling_parent_and_container_entries_and_wrong_count(self):
        text = MAP.replace("          - 5\n", "          - 77\n").replace("      parent: 6\n", "      parent: 66\n")
        problems = conv.verify(conv.MapDoc(text.replace("entityCount: 9", "entityCount: 12")))
        self.assertTrue(any("parent 66" in p for p in problems))
        self.assertTrue(any("77" in p for p in problems))
        self.assertTrue(any("entityCount" in p for p in problems))

    def test_buckle_references_are_followed(self):
        text = MAP.replace("- proto: Seat2\n", "- proto: Seat2\n  entities:\n  - uid: 20\n    components:\n    - type: Transform\n"
                           "      parent: 2\n    - type: Buckle\n      buckledTo: 6\n  - uid: 21\n    components:\n    - type: Strap\n"
                           "      buckledEntities:\n      - 6\n      - 3\n- proto: Gap\n").replace("entityCount: 9", "entityCount: 11")
        doc, _ = run_conversion(text)
        out = doc.render()
        self.assertIn("      buckledTo: null\n", out)
        self.assertIn("      buckledEntities:\n      - 3\n", out)
        self.assertEqual([p for p in conv.verify(doc) if "not in the map" in p], [])


class ScanTest(unittest.TestCase):
    def test_component_names(self):
        names = conv.component_names_in(
            '[RegisterComponent]\npublic sealed partial class FooComponent : Component { }\n'
            '[RegisterComponent, NetworkedComponent]\n[Access(typeof(X))]\npublic sealed partial class Bar : Component { }\n'
            '[ComponentProtoName("Baz")]\n[RegisterComponent]\npublic sealed partial class QuxComponent : Component { }\n'
            'public sealed class NotAComponent { }\n')
        self.assertEqual(names, {"Foo", "Bar", "Baz"})

    def test_prototype_ids_ignore_other_kinds_and_keep_order_free(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "x.yml"
            path.write_text("- type: entity\n  id: A\n\n- id: B\n  type: entity\n\n- type: reagent\n  id: C\n\n"
                            "- type: tile\n  id: D\n", encoding="utf-8")
            found = conv.scan_prototype_ids(Path(tmp))
        self.assertEqual(found["entity"], {"A", "B"})
        self.assertEqual(found["tile"], {"D"})

    def test_output_name_prefixes_cmu_maps(self):
        sources = {"cmu": {"path": "/x/cmu"}, "rmc": {"path": "/x/rmc"}}
        self.assertEqual(conv.output_name(Path("/x/cmu/Maps/Ambulance.yml"), sources, {}), "cmu_ambulance.yml")
        self.assertEqual(conv.output_name(Path("/x/rmc/Maps/van_interior.yml"), sources, {}), "van_interior.yml")


SPACE, FLOOR, VOID = 1, 2, 3
DEFAULT_TILEMAP = "  1: Space\n  2: FloorSteel\n  3: RMCVoid\n"


def chunk_b64(tiles: dict, fill: int = SPACE) -> str:
    """One 16 by 16 chunk in the version 7 byte layout (id, flags, variant, rotation), `tiles` holding (x, y) -> id."""
    data = bytearray()
    for y in range(16):
        for x in range(16):
            data += tiles.get((x, y), fill).to_bytes(4, "little", signed=True) + bytes(3)
    return base64.b64encode(bytes(data)).decode("ascii")


def grounding_map(tiles: dict, placed: list, grid_transform: str = "", tilemap: str = DEFAULT_TILEMAP) -> str:
    """A map with the map entity (1), the grid (2) and `placed` entities as (proto, uid, parent, "x,y") from uid 3 on."""
    text = ("meta:\n  format: 7\n  category: Map\n  engineVersion: 264.0.2\n  forkId: \"\"\n  forkVersion: \"\"\n"
            f"  time: 03/18/2026 20:29:20\n  entityCount: {2 + len(placed)}\nmaps:\n- 1\ngrids:\n- 2\norphans: []\nnullspace: []\n"
            f"tilemap:\n{tilemap}entities:\n- proto: \"\"\n  entities:\n  - uid: 1\n    components:\n    - type: MetaData\n"
            "      name: Map Entity\n    - type: Transform\n    - type: Map\n      mapPaused: True\n  - uid: 2\n    components:\n"
            f"    - type: MetaData\n      name: grid\n    - type: Transform\n{grid_transform}      parent: 1\n    - type: MapGrid\n"
            f"      chunks:\n        0,0:\n          ind: 0,0\n          tiles: {chunk_b64(tiles)}\n          version: 7\n")
    for proto, uid, parent, pos in placed:
        text += f"- proto: {proto}\n  entities:\n  - uid: {uid}\n    components:\n    - type: Transform\n"
        text += f"      pos: {pos}\n      parent: {parent}\n"
    return text


def tile_names(doc: "conv.MapDoc", *tiles):
    grid = next(e for _, e in doc.entities() if "MapGrid" in e.component_names())
    grid_tiles = conv.GridTiles(grid, doc.tilemap())
    return [grid_tiles.name(t) for t in tiles]


class CabinTileTest(unittest.TestCase):
    ROOM = {(2, 2): FLOOR, (3, 2): FLOOR, (2, 3): FLOOR, (3, 3): FLOOR}

    def ground(self, text, cabin="RMCVoid"):
        doc = conv.MapDoc(text)
        report = conv.Report()
        conv.ground_placed_entities(doc, cabin, report, "t.yml")
        return doc, report

    def test_a_seat_over_an_empty_tile_next_to_the_cabin_gets_the_cabin_tile(self):
        doc, report = self.ground(grounding_map(self.ROOM, [("Seat", 3, 2, "1.5,2.5")]))
        self.assertEqual(tile_names(doc, (1, 2), (2, 2), (0, 2)), ["RMCVoid", "FloorSteel", "Space"])
        self.assertEqual(dict(report.grounded), {"Seat": 1})
        self.assertEqual(report.not_grounded, [])
        self.assertEqual(conv.verify(doc), [])

    def test_only_the_chunk_line_changes(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 2, "1.5,2.5")])
        doc, _ = self.ground(text)
        changed = [(a, b) for a, b in zip(text.split("\n"), doc.render().split("\n")) if a != b]
        self.assertEqual(len(changed), 1)
        self.assertTrue(changed[0][0].startswith("          tiles: "))

    def test_an_entity_parented_to_the_map_is_placed_through_the_grid_transform(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 1, "6.5,7.5")], grid_transform="      pos: 5,5\n")
        doc, report = self.ground(text)
        self.assertEqual(tile_names(doc, (1, 2)), ["RMCVoid"])
        self.assertEqual(dict(report.grounded), {"Seat": 1})

    def test_a_turned_grid_is_taken_into_account(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 1, "7.5,11.5")],
                             grid_transform="      rot: 1.5707963267948966 rad\n      pos: 10,10\n")
        doc, _ = self.ground(text)
        self.assertEqual(tile_names(doc, (1, 2), (0, 2)), ["RMCVoid", "Space"])

    def test_a_tile_that_touches_nothing_is_left_alone_and_reported(self):
        text = grounding_map(self.ROOM, [("Marker", 3, 2, "8.5,8.5")])
        doc, report = self.ground(text)
        self.assertEqual(doc.render(), text)
        self.assertEqual(len(report.not_grounded), 1)
        self.assertIn("tile 8,8 under Marker", report.not_grounded[0])

    def test_a_tile_that_touches_a_laid_tile_is_laid_too(self):
        placed = [("Seat", 3, 2, "0.5,2.5"), ("Seat", 4, 2, "1.5,2.5")]
        doc, report = self.ground(grounding_map(self.ROOM, placed))
        self.assertEqual(tile_names(doc, (0, 2), (1, 2)), ["RMCVoid", "RMCVoid"])
        self.assertEqual(report.grounded_tiles, 2)

    def test_two_entities_on_one_tile_lay_one_tile(self):
        placed = [("Seat", 3, 2, "1.2,2.5"), ("Locker", 4, 2, "1.8,2.5")]
        doc, report = self.ground(grounding_map(self.ROOM, placed))
        self.assertEqual(report.grounded_tiles, 1)
        self.assertEqual(dict(report.grounded), {"Seat": 1, "Locker": 1})

    def test_contents_of_containers_and_children_are_ignored(self):
        text = grounding_map(self.ROOM, [("Crate", 3, 2, "2.5,2.5"), ("Gadget", 4, 3, "1.5,2.5")])
        doc, report = self.ground(text)
        self.assertEqual(doc.render(), text)
        self.assertEqual(report.grounded_tiles, 0)

    def test_a_tile_that_is_already_cabin_is_not_touched(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 2, "2.5,2.5")])
        doc, report = self.ground(text)
        self.assertEqual(doc.render(), text)
        self.assertEqual(report.grounded_tiles, 0)

    def test_the_cabin_tile_is_added_to_the_tilemap_when_the_map_has_none(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 2, "1.5,2.5")], tilemap="  1: Space\n  2: FloorSteel\n")
        doc, _ = self.ground(text)
        self.assertEqual(doc.tilemap(), {1: "Space", 2: "FloorSteel", 3: "RMCVoid"})
        self.assertIn("tilemap:\n  1: Space\n  2: FloorSteel\n  3: RMCVoid\nentities:\n", doc.render())
        self.assertEqual(tile_names(doc, (1, 2)), ["RMCVoid"])

    def test_a_tile_in_a_chunk_the_grid_lacks_is_reported(self):
        doc, report = self.ground(grounding_map(self.ROOM, [("Seat", 3, 2, "20.5,2.5")]))
        self.assertEqual(report.grounded_tiles, 0)
        self.assertIn("chunk the grid does not have", report.not_grounded[0])

    def test_a_chunk_in_another_form_is_reported_not_edited(self):
        text = MAP
        doc = conv.MapDoc(text)
        report = conv.Report()
        conv.ground_placed_entities(doc, "RMCVoid", report, "t.yml")
        self.assertEqual(doc.render(), text)
        self.assertIn("version 7", report.not_grounded[0])

    def test_crlf_input_stays_crlf(self):
        doc, _ = self.ground(grounding_map(self.ROOM, [("Seat", 3, 2, "1.5,2.5")]).replace("\n", "\r\n"))
        out = doc.render()
        self.assertEqual(out.count("\r\n"), out.count("\n"))
        self.assertNotIn("\r\r", out)

    def convert(self, text, mapping):
        with tempfile.TemporaryDirectory() as tmp:
            know = make_knowledge(Path(tmp))
            doc = conv.MapDoc(text)
            report = conv.Report()
            conv.convert(doc, mapping, know, report, "t.yml")
            return doc, report

    def test_the_mapping_key_turns_it_on(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 2, "1.5,2.5")])
        off, _ = self.convert(text, MAPPING)
        on, report = self.convert(text, dict(MAPPING, cabin_tile="RMCVoid"))
        self.assertEqual(tile_names(off, (1, 2)), ["Space"])
        self.assertEqual(tile_names(on, (1, 2)), ["RMCVoid"])
        self.assertEqual(report.grounded_tiles, 1)
        self.assertIn("cabin tile laid under 1 tile(s)", "\n".join(report.lines()))

    def test_an_entity_the_conversion_deletes_grounds_nothing(self):
        text = grounding_map(self.ROOM, [("RMCGadget", 3, 2, "1.5,2.5")])
        doc, report = self.convert(text, dict(MAPPING, cabin_tile="RMCVoid"))
        self.assertEqual(tile_names(doc, (1, 2)), ["Space"])
        self.assertEqual(report.grounded_tiles, 0)


class AdoptMapParentedTest(unittest.TestCase):
    """What the source parented to the map entity ends up on the grid, or is deleted."""

    ROOM = {(2, 2): FLOOR, (3, 2): FLOOR, (2, 3): FLOOR, (3, 3): FLOOR}
    ON = dict(MAPPING, cabin_tile="RMCVoid")

    def convert(self, text, mapping=None):
        with tempfile.TemporaryDirectory() as tmp:
            know = make_knowledge(Path(tmp))
            doc = conv.MapDoc(text)
            report = conv.Report()
            conv.convert(doc, mapping or self.ON, know, report, "t.yml")
            return doc, report

    @staticmethod
    def placement_of(doc, uid):
        entity = next(e for _, e in doc.entities() if e.uid == uid)
        return conv.placement(entity)

    def test_an_entity_over_the_cabin_goes_onto_the_grid_in_the_grids_frame(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 1, "7.5,7.5")], grid_transform="      pos: 5,5\n")
        doc, report = self.convert(text)
        self.assertEqual(self.placement_of(doc, 3), (2, (2.5, 2.5), 0.0))
        self.assertEqual(dict(report.adopted), {"Seat": 1})
        self.assertEqual(conv.verify(doc), [])

    def test_a_turned_grid_turns_the_position_and_the_rotation(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 1, "7.5,11.5")],
                             grid_transform="      rot: 1.5707963267948966 rad\n      pos: 10,10\n")
        doc, _ = self.convert(text)
        parent, pos, rot = self.placement_of(doc, 3)
        self.assertEqual(parent, 2)
        self.assertAlmostEqual(pos[0], 1.5, places=6)
        self.assertAlmostEqual(pos[1], 2.5, places=6)
        self.assertAlmostEqual(rot, -1.5707963, places=6)

    def test_an_existing_rotation_line_is_kept_while_the_grid_is_straight(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 1, "2.5,2.5")]).replace(
            "    - type: Transform\n      pos: 2.5,2.5\n", "    - type: Transform\n      rot: 3.141592653589793 rad\n      pos: 2.5,2.5\n")
        doc, _ = self.convert(text)
        self.assertIn("      rot: 3.141592653589793 rad\n      pos: 2.5,2.5\n      parent: 2\n", doc.render())

    def test_an_entity_over_no_cabin_tile_is_deleted_with_what_hangs_off_it(self):
        placed = [("Seat", 3, 1, "2.5,2.5"), ("Wall", 4, 1, "9.5,9.5"), ("Gadget", 5, 4, "0,0"), ("Seat", 6, 2, "3.5,3.5")]
        doc, report = self.convert(grounding_map(self.ROOM, placed))
        uids = {e.uid for _, e in doc.entities()}
        self.assertEqual(uids, {1, 2, 3, 6})
        self.assertEqual(dict(report.strays), {"Wall": 1})
        self.assertEqual(report.removed["Gadget (child of a deleted entity)"], 1)
        self.assertIn("  entityCount: 4\n", doc.render())
        self.assertEqual(conv.verify(doc), [])

    def test_what_is_already_on_the_grid_or_in_something_is_left_alone(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 2, "2.5,2.5"), ("Gadget", 4, 3, "0.5,0.5")])
        doc, report = self.convert(text)
        self.assertEqual(doc.render(), text)
        self.assertFalse(report.adopted)
        self.assertFalse(report.strays)

    def test_the_other_lines_of_the_transform_stay(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 1, "2.5,2.5")]).replace(
            "      pos: 2.5,2.5\n      parent: 1\n", "      pos: 2.5,2.5\n      parent: 1\n      anchored: True\n")
        doc, _ = self.convert(text)
        self.assertIn("      pos: 2.5,2.5\n      parent: 2\n      anchored: True\n", doc.render())

    def test_a_position_left_out_because_it_was_zero_is_written(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 1, "9,9")], grid_transform="      pos: -2.5,-2.5\n")
        text = text.replace("      pos: 9,9\n", "")
        doc, _ = self.convert(text)
        self.assertEqual(self.placement_of(doc, 3), (2, (2.5, 2.5), 0.0))

    def test_the_old_behaviour_is_off_without_the_cabin_tile(self):
        text = grounding_map(self.ROOM, [("Seat", 3, 1, "2.5,2.5")])
        doc, report = self.convert(text, MAPPING)
        self.assertIn("      parent: 1\n", doc.render())
        self.assertFalse(report.adopted)
        self.assertTrue(any("parented to the map" in p for p in conv.verify(doc)))

    def test_crlf_input_stays_crlf(self):
        doc, _ = self.convert(grounding_map(self.ROOM, [("Seat", 3, 1, "2.5,2.5")]).replace("\n", "\r\n"))
        out = doc.render()
        self.assertEqual(out.count("\r\n"), out.count("\n"))
        self.assertNotIn("\r\r", out)


class FormatFloatTest(unittest.TestCase):
    def test_formats_like_the_maps(self):
        self.assertEqual(conv.format_float(2.0), "2")
        self.assertEqual(conv.format_float(-0.0), "0")
        self.assertEqual(conv.format_float(0.27478169999999996), "0.2747817")
        self.assertEqual(conv.format_float(-2.5), "-2.5")


if __name__ == "__main__":
    unittest.main()
