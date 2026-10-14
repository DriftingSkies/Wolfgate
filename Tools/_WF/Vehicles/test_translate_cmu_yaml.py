"""Tests for translate_cmu_yaml.py. Run from this folder: python -m unittest test_translate_cmu_yaml -v"""
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import translate_cmu_yaml as tr  # noqa: E402

RULES = {
    "parents": {"OldBase": "VehicleTankBase"},
    "rename_components": {"Vehicle": "RMCVehicle"},
    "drop_components": {"TankCookOff": "cook-off", "HardpointState": "made by RMC", "ContainerContainer": "made by RMC",
                        "Injurable": "folded", "GunIFF": "no IFF"},
    "drop_fields": {"HardpointItem": {"damageRegion": "directional damage"}, "HardpointIntegrity": {"integrity": "RMC initialises"},
                    "VehicleEnter": {"maxXenos": "no xenos"}, "Vehicle": {"transferDamage": "ride-on option"}},
    "drop_slot_fields": {"disableEject": "CMU-only"},
    "drop_list_values": {"BarricadeImpassable": "no such bit"},
    "drop_tags": {"VehiclePlow": "no plows"},
    "drop_if_empty": ["HardpointIntegrity", "VehicleHardpointVisuals", "Tag"],
    "inherit_components": ["GridVehicleMover"],
    "value_renames": {"StructuralMarine": "StructuralInorganic"},
    "fix_values": {"flag_2": "flag_1"},
    "hardpoint_id_fields": {"hardpointType": "HardpointType", "slotType": "HardpointSlotType", "vehicleFamily": "HardpointVehicleFamily",
                            "compatibilityId": "HardpointCompatibility"},
    "sound_substitutions": {"/Audio/CMU14/Weapons/vehicle/gun.ogg": "/Audio/_WF/Vehicles/weapons/cannon_fire1.ogg"},
    "sound_collection_substitutions": {"L98FireSound": "/Audio/_WF/Vehicles/guns/gun_ml66a_2.ogg"},
    "check_fields": ["VehicleEnter"],
    "entry_classes": {"HardpointSlots.slots": "HardpointSlot"},
    "add_compatibility": {"TwoTank": "HardpointCompatibilityTWE"},
    "extra_prototypes": [{"id": "HardpointCompatibilityTWE", "text": "x"}],
}

CS = """
[RegisterComponent]
public sealed partial class VehicleEnterComponent : Component
{
    [DataField]
    public ResPath InteriorPath;

    [DataField, AutoNetworkedField]
    public int MaxPassengers = 4;

    [DataField("enterDoAfter")]
    public float EnterTime = 1f;

    [DataField]
    public List<VehicleEntryPoint> EntryPoints = new();
}

[DataDefinition]
public sealed partial class HardpointSlot
{
    [DataField(required: true)]
    public string Id { get; set; } = string.Empty;

    [DataField]
    public EntProtoId? HardpointType { get; set; }

    [DataField]
    public EntProtoId? SlotType { get; set; }

    [DataField]
    public EntProtoId? CompatibilityId { get; set; }

    [DataField]
    public bool Required { get; set; } = true;

    [DataField]
    public float InsertDelay { get; set; } = 1f;
}
"""

TANK = """- type: entity
  parent: OldBase
  id: TwoTank
  name: two
  components:
  - type: TankCookOff # CMU14
    turretSprite:
      sprite: CMU14/Structures/vehicles/fv150/exterior.rsi
  - type: HardpointState
  - type: ContainerContainer
    containers:
      armor: !type:ContainerSlot
        ent: null
  - type: HardpointIntegrity
    integrity: 1000
  - type: Injurable
    damageContainer: StructuralMarine
  - type: Damageable
    damageModifierSet: VehicleFrameTank
  - type: GridVehicleMover
    maxSpeed: 8
    canSmashWalls: true
  - type: VehicleEnter
    interiorPath: /Maps/CMU14/Vehicles/Interiors/arv.yml
    maxPassengers: 4
    maxXenos: 2
    mysteryField: 1
    entryPoints:
    - offset: 0,2
      radius: 1
    - offset: 0,2
      radius: 1
    - offset: 1,1
      radius: 1
  - type: Fixtures
    fixtures:
      world:
        mask:
        - Impassable
        - BarricadeImpassable
  - type: Sprite
    sprite: _RMC14/Structures/Vehicles/tank.rsi
    layers:
    - state: flag_2
  - type: HardpointSlots
    vehicleFamily: Tank
    slots:
    - id: primary
      hardpointType: Turret
      slotType: Turret
      disableEject: true

      insertDelay: 3.0
    - id: wheel-1
      hardpointType: Wheel
      slotType: Treads
      compatibilityId: SPPTank
  - type: Gun
    soundGunshot: /Audio/CMU14/Weapons/vehicle/gun.ogg
  - type: Tag
    tags:
    - VehiclePlow
  - type: GunIFF
    enabled: true

- type: soundCollection
  id: Whatever
  files: []

- type: entity
  parent: VehicleTankBase
  id: Other
  components:
  - type: Vehicle
    transferDamage: true
    requiresHands: false
  - type: Gun
    soundGunshot:
      collection: L98FireSound
  - type: HardpointItem
    damageRegion: Front
    hardpointType: HardpointTypeTurret
    slotType: Cannon
"""


def make_translator(extra_known=None) -> tr.Translator:
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        (root / "cs").mkdir()
        (root / "cs/V.cs").write_text(CS, encoding="utf-8")
        fields = tr.RmcFields(root, ["cs"])
    assets = {"_RMC14/Structures/Vehicles/tank.rsi": "_WF/Vehicles/tank.rsi",
              "CMU14/Structures/vehicles/fv150/exterior.rsi": "_WF/Vehicles/CMU/fv150/exterior.rsi"}
    known = {"HardpointTypeTurret", "HardpointTypeWheel", "HardpointSlotTypeTurret", "HardpointSlotTypeTreads",
             "HardpointSlotTypeCannon", "HardpointVehicleFamilyTank", "HardpointCompatibilitySPPTank"}
    components = {"Sprite", "Damageable", "VehicleEnter", "Fixtures", "HardpointSlots", "Gun", "RMCVehicle", "HardpointItem"}
    return tr.Translator(RULES, fields, known, assets, components | (extra_known or set()), {"VehicleTankBase", "Other"})


class FieldScanTest(unittest.TestCase):
    def test_reads_registered_components_and_datafields(self):
        reg, fields = tr.scan_fields(CS)
        self.assertEqual(reg, {"VehicleEnter": "VehicleEnterComponent"})
        self.assertEqual(fields["VehicleEnterComponent"], {"interiorPath", "maxPassengers", "enterDoAfter", "entryPoints"})
        self.assertEqual(fields["HardpointSlot"], {"id", "hardpointType", "slotType", "compatibilityId", "required", "insertDelay"})


class TranslateTest(unittest.TestCase):
    def setUp(self):
        self.t = make_translator()
        self.out = self.t.translate_text(TANK, None)
        self.report = self.t.finish()

    def test_reparents(self):
        self.assertIn("  parent: VehicleTankBase\n  id: TwoTank", self.out)
        self.assertNotIn("OldBase", self.out)

    def test_drops_cmu_components_and_says_why(self):
        for name in ("TankCookOff", "HardpointState", "ContainerContainer", "Injurable", "GunIFF"):
            self.assertNotIn(f"type: {name}", self.out)
        whys = {(e, w): y for e, w, y in self.report.dropped}
        self.assertEqual(whys[("TwoTank", "component TankCookOff")], "cook-off")

    def test_drops_fields_and_empty_components(self):
        self.assertNotIn("integrity: 1000", self.out)
        self.assertNotIn("type: HardpointIntegrity", self.out)
        self.assertNotIn("maxXenos", self.out)
        self.assertNotIn("damageRegion", self.out)
        self.assertNotIn("transferDamage", self.out)

    def test_drops_a_field_rmc_does_not_declare(self):
        self.assertNotIn("mysteryField", self.out)
        self.assertIn("      maxPassengers: 4\n".replace("      ", "    "), self.out)
        self.assertTrue(any(w == "field VehicleEnter.mysteryField" and "no such field" in y for _, w, y in self.report.dropped))

    def test_a_declared_field_with_an_explicit_name_is_kept(self):
        text = TANK.replace("    maxPassengers: 4\n", "    enterDoAfter: 1\n")
        out = make_translator().translate_text(text, None)
        self.assertIn("    enterDoAfter: 1\n", out)

    def test_mover_numbers_go_to_the_report(self):
        self.assertNotIn("GridVehicleMover", self.out)
        what = next(w for e, w, _ in self.report.dropped if w.startswith("component GridVehicleMover"))
        self.assertIn("maxSpeed=8", what)
        self.assertIn("canSmashWalls=true", what)

    def test_folds_injurable_into_damageable(self):
        self.assertIn("  - type: Damageable\n    damageContainer: StructuralInorganic\n    damageModifierSet: VehicleFrameTank\n", self.out)

    def test_prefixes_hardpoint_ids(self):
        self.assertIn("    vehicleFamily: HardpointVehicleFamilyTank\n", self.out)
        self.assertIn("      hardpointType: HardpointTypeTurret\n", self.out)
        self.assertIn("      slotType: HardpointSlotTypeTreads\n", self.out)
        self.assertIn("      compatibilityId: HardpointCompatibilitySPPTank\n", self.out)

    def test_adds_compatibility_to_every_slot(self):
        self.assertEqual(self.out.count("compatibilityId: HardpointCompatibilityTWE\n"), 1)
        self.assertIn("    - id: primary\n      compatibilityId: HardpointCompatibilityTWE\n", self.out)
        self.assertIn("      compatibilityId: HardpointCompatibilitySPPTank\n", self.out)  # a slot's own id is kept

    def test_drops_slot_field_and_blank_lines_inside_blocks(self):
        self.assertNotIn("disableEject", self.out)
        self.assertNotIn("true\n\n      insertDelay", self.out)
        self.assertNotIn("\n\n      ", self.out)

    def test_dedupes_entry_points(self):
        self.assertEqual(self.out.count("offset: 0,2"), 1)
        self.assertIn("offset: 1,1", self.out)

    def test_removes_list_values_tags_and_flags_missing_vehicle_layer(self):
        self.assertNotIn("BarricadeImpassable", self.out)
        self.assertNotIn("VehiclePlow", self.out)
        self.assertNotIn("type: Tag", self.out)
        self.assertTrue(any("without the Vehicle layer" in n for n in self.report.notes))

    def test_rewrites_assets_maps_and_sounds(self):
        self.assertIn("sprite: _WF/Vehicles/tank.rsi", self.out)
        self.assertIn("interiorPath: /Maps/_WF/Vehicles/Interiors/cmu_arv.yml", self.out)
        self.assertIn("soundGunshot: /Audio/_WF/Vehicles/weapons/cannon_fire1.ogg", self.out)
        self.assertIn("    soundGunshot:\n      path: /Audio/_WF/Vehicles/guns/gun_ml66a_2.ogg\n", self.out)

    def test_fixes_known_data_bugs(self):
        self.assertIn("state: flag_1", self.out)

    def test_renames_component_and_drops_ride_on_options(self):
        self.assertIn("  - type: RMCVehicle\n", self.out)

    def test_reports_unknown_hardpoint_ids_and_components(self):
        self.assertNotIn("slotType", self.report.unknown_ids)
        t = make_translator()
        t.translate_text(TANK.replace("slotType: Treads", "slotType: Wings"), None)
        self.assertEqual(t.finish().unknown_ids["slotType"], {"HardpointSlotTypeWings"})

    def test_unknown_components_are_listed(self):
        t = make_translator()
        t.translate_text(TANK.replace("type: Damageable", "type: Mystery"), {"TwoTank"})
        self.assertIn("Mystery", t.finish().unknown_components)

    def test_non_entity_items_pass_through_the_report_only(self):
        self.assertIn("soundCollection item", self.report.passed_through)
        self.assertNotIn("soundCollection", self.out)

    def test_ids_filter_and_unresolved_assets(self):
        t = make_translator()
        out = t.translate_text(TANK.replace("tank.rsi", "mystery.rsi"), {"TwoTank"})
        self.assertNotIn("id: Other", out)
        self.assertEqual(t.report.entities, 1)
        self.assertTrue(any(p.endswith("mystery.rsi") for _, p in t.report.unresolved))

    def test_references_to_undefined_ids(self):
        t = make_translator()
        t.translate_text(TANK.replace("parent: OldBase", "parent: VehicleTankBase").replace(
            "  - type: Gun\n", "  - type: Gun\n    proto: NotDefined\n"), {"TwoTank"})
        self.assertEqual(set(t.finish().referenced), {"NotDefined"})
        t2 = make_translator()
        t2.translate_text(TANK, None)
        self.assertEqual(t2.finish().referenced, {})


class NewlineTest(unittest.TestCase):
    def test_crlf_in_crlf_out_and_no_doubled_cr(self):
        out = make_translator().translate_text(TANK.replace("\n", "\r\r\n"), None)
        self.assertEqual(out.count("\r\n"), out.count("\n"))
        self.assertNotIn("\r\r", out)

    def test_lf_stays_lf(self):
        self.assertNotIn("\r", make_translator().translate_text(TANK, None))

    def test_a_file_with_no_selected_entity_is_empty(self):
        self.assertEqual(make_translator().translate_text(TANK, {"Nope"}), "")


class RulesTest(unittest.TestCase):
    def test_the_shipped_rules_load_and_name_a_complete_roster(self):
        rules = tr.load_rules()
        self.assertEqual(sum(len(e.get("ids") or []) for e in rules["roster"] if e["file"].endswith("Hardpoints/hardpoints_base.yml")), 3)
        ids = [i for e in rules["roster"] if "Hardpoints" in e["file"] for i in e["ids"]]
        self.assertEqual(len(ids), 11)
        for prefix in rules["hardpoint_id_fields"].values():
            self.assertTrue(prefix.startswith("Hardpoint"))


if __name__ == "__main__":
    unittest.main()
