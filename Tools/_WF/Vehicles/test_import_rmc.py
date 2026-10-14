"""Tests for import_rmc.py. Run from this folder: python -m unittest test_import_rmc -v"""
import re
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import import_rmc as imp  # noqa: E402


class MaskCodeTest(unittest.TestCase):
    def test_keeps_length_and_lines(self):
        text = 'var a = "x{"; // c {\nvar b = 1; /* {\n} */ var c = \'}\';\n'
        masked = imp.mask_code(text)
        self.assertEqual(len(masked), len(text))
        self.assertEqual(masked.count("\n"), text.count("\n"))
        self.assertNotIn("{", masked)
        self.assertNotIn("}", masked)

    def test_interpolated_string_with_nested_quotes_and_braces(self):
        text = 'var s = $"a {(x ? "}" : "{")} b {{ c"; int closing = 1; }'
        masked = imp.mask_code(text)
        self.assertEqual(masked.count("}"), 1)
        self.assertIn("int closing", masked)

    def test_verbatim_and_raw_strings(self):
        text = 'var v = @"a ""} b"; var r = """ x " } """; int tail = 2;'
        masked = imp.mask_code(text)
        self.assertNotIn("}", masked)
        self.assertIn("int tail", masked)


class DependencyTest(unittest.TestCase):
    def test_drops_readonly_only_on_dependencies(self):
        text = (
            "class A {\n"
            "    [Dependency] private readonly IFoo _foo = default!;\n"
            "    [Dependency]\n"
            "    protected readonly Bar _bar = default!;\n"
            "    private readonly int _plain = 1;\n"
            "}\n"
        )
        new, count = imp.drop_readonly_dependencies(text)
        self.assertEqual(count, 2)
        self.assertIn("[Dependency] private IFoo _foo = default!;", new)
        self.assertIn("protected Bar _bar = default!;", new)
        self.assertIn("private readonly int _plain = 1;", new)

    def test_ignores_dependency_inside_comments_and_strings(self):
        text = '// [Dependency] private readonly X _x = default!;\nvar s = "[Dependency] readonly";\n'
        self.assertEqual(imp.drop_readonly_dependencies(text), (text, 0))

    def test_adds_partial_to_the_type_and_its_parents(self):
        text = (
            "namespace N;\n"
            "public sealed class Outer\n"
            "{\n"
            "    public sealed class Inner : EntitySystem\n"
            "    {\n"
            "        [Dependency] private IFoo _foo = default!;\n"
            "    }\n"
            "\n"
            "    public sealed class Bystander { int x; }\n"
            "}\n"
        )
        new, count = imp.add_partial_to_dependency_types(text)
        self.assertEqual(count, 2)
        self.assertIn("public sealed partial class Outer", new)
        self.assertIn("public sealed partial class Inner", new)
        self.assertIn("public sealed class Bystander", new)

    def test_partial_types_and_dependency_free_types_are_left_alone(self):
        text = "public sealed partial class A : EntitySystem { [Dependency] private IFoo _f = default!; }\npublic sealed class B { }\n"
        self.assertEqual(imp.add_partial_to_dependency_types(text), (text, 0))

    def test_generic_constraints_are_not_types(self):
        text = (
            "public sealed class A<T> : EntitySystem where T : class\n"
            "{\n"
            "    [Dependency] private IFoo _foo = default!;\n"
            "}\n"
        )
        new, count = imp.add_partial_to_dependency_types(text)
        self.assertEqual(count, 1)
        self.assertIn("public sealed partial class A<T>", new)
        self.assertIn("where T : class", new)

    def test_braces_in_strings_do_not_confuse_the_body(self):
        text = (
            'public class A\n{\n    string s = "}";\n}\n'
            "public class B\n{\n    [Dependency] private IFoo _foo = default!;\n}\n"
        )
        new, count = imp.add_partial_to_dependency_types(text)
        self.assertEqual(count, 1)
        self.assertIn("public class A", new)
        self.assertIn("public partial class B", new)


RULES = {
    "qualified": {
        "Content.Shared.Vehicle.VehicleSystem": "Content.Shared._WF.Vehicles.RMCVehicleSystem",
    },
    "prefix": {
        "Content.Shared.Vehicle.Components": "Content.Shared._WF.Vehicles",
        "Content.Shared.Vehicle": "Content.Shared._WF.Vehicles",
        "Content.Shared._RMC14.Vehicle": "Content.Shared._WF.Vehicles",
        "Content.Client._RMC14.Vehicle": "Content.Client._WF.Vehicles",
    },
    "exact": {"Content.Shared._RMC14.Weapons.Ranged": "Content.Shared._WF.Vehicles"},
    "types": {"VehicleComponent": "RMCVehicleComponent", "VehicleVisuals": "RMCVehicleVisuals"},
    "calls": {r"\b_physics\.IsHardCollidable\(": "VehicleHardCollision.IsHardCollidable("},
}


class RewriteTest(unittest.TestCase):
    def run_transform(self, text, rel="Content.Shared/_RMC14/Vehicle/Core/X.cs"):
        return imp.transform(text, rel, ".cs", RULES)[0]

    def test_namespaces_and_usings_merge(self):
        text = (
            "﻿using Content.Shared.Vehicle.Components;\r\n"
            "using Content.Shared._RMC14.Vehicle;\r\n"
            "using Content.Shared.Vehicle;\r\n"
            "using Content.Shared._RMC14.Weapons.Ranged;\r\n"
            "using Content.Shared._RMC14.Weapons.Ranged.Prediction;\r\n"
            "\r\n"
            "namespace Content.Shared._RMC14.Vehicle;\r\n"
        )
        new = self.run_transform(text)
        self.assertNotIn("\r", new)
        self.assertNotIn("﻿", new)
        self.assertNotIn("using Content.Shared._WF.Vehicles;", new)
        self.assertIn("namespace Content.Shared._WF.Vehicles;", new)
        self.assertIn("using Content.Shared._RMC14.Weapons.Ranged.Prediction;", new)

    def test_base_layer_files_rename_to_rmc_vehicle_system(self):
        text = "[Access(typeof(VehicleSystem))] class VehicleComponent { VehicleVisuals.CanRun }"
        new = self.run_transform(text, "Content.Shared/Vehicle/Components/VehicleComponent.cs")
        self.assertEqual(
            new, "[Access(typeof(RMCVehicleSystem))] class RMCVehicleComponent { RMCVehicleVisuals.CanRun }")

    def test_other_files_rename_to_the_interior_system(self):
        text = "[Dependency] private VehicleSystem _vehicle = default!;\nContent.Shared.Vehicle.VehicleSystem _base;"
        new = self.run_transform(text)
        self.assertIn("VehicleInteriorSystem _vehicle", new)
        self.assertIn("Content.Shared._WF.Vehicles.RMCVehicleSystem _base", new)

    def test_whole_word_only(self):
        text = "GridVehicleOperatorComponent VehicleComponentX RMCVehicleComponent VehicleWeaponsSystem"
        self.assertEqual(self.run_transform(text), text)

    def test_hard_collidable_call_is_redirected(self):
        text = "_physics.IsHardCollidable((a, b, c), (d, e, f))"
        self.assertEqual(self.run_transform(text), "VehicleHardCollision.IsHardCollidable((a, b, c), (d, e, f))")

    def test_idempotent(self):
        text = (
            "using Content.Shared.Vehicle.Components;\n"
            "namespace Content.Shared._RMC14.Vehicle;\n"
            "public sealed class S : EntitySystem\n{\n"
            "    [Dependency] private readonly VehicleSystem _v = default!;\n"
            "    Entity<VehicleComponent> e;\n}\n"
        )
        once = self.run_transform(text)
        self.assertEqual(self.run_transform(once), once)

    def test_xaml_namespace_lines(self):
        text = 'xmlns:controls="clr-namespace:Content.Client._RMC14.Vehicle.Ui"'
        new = imp.transform(text, "Content.Client/_RMC14/Vehicle/Ui/X.xaml", ".xaml", RULES)[0]
        self.assertEqual(new, 'xmlns:controls="clr-namespace:Content.Client._WF.Vehicles.Ui"')

    def test_tidy_usings_keeps_aliases_and_statics(self):
        text = "using A.B;\nusing A.B;\nusing static C.D;\nusing E = F.G;\nnamespace A.H;\n"
        new = imp.tidy_usings(text)
        self.assertEqual(new.count("using A.B;"), 1)
        self.assertIn("using static C.D;", new)
        self.assertIn("using E = F.G;", new)


class ConfigTest(unittest.TestCase):
    config = imp.load_config()

    def test_every_destination_is_in_the_vehicles_module_and_unique(self):
        destinations = [item.dest for item in imp.plan(self.config, None, {})]
        self.assertEqual(len(destinations), len(set(destinations)))
        for dest in destinations:
            self.assertRegex(dest, r"^(Content\.(Shared|Server|Client)|Resources/\w+)/_WF/Vehicles/", dest)
        for entry in self.config.get("derived") or []:
            self.assertIn("/_WF/Vehicles/", entry["dest"])

    def test_every_source_exists_when_the_repos_are_present(self):
        for item in imp.plan(self.config, None, {}):
            root = Path(self.config["sources"][item.source]["path"])
            if not root.exists():
                self.skipTest(f"{root} is not checked out")
            self.assertTrue(item.src.exists(), item.rel_source)

    def test_rewrite_rules_target_the_module_namespaces(self):
        rules = self.config["rewrite"]
        for table in ("prefix", "exact"):
            for new in rules[table].values():
                self.assertRegex(new, r"^Content\.(Shared|Server|Client)\._WF\.Vehicles(\.\w+)*$")

    def test_locale_files_with_rmc_keys_have_a_manifest_row(self):
        rows = {row[0] for row in imp.manifest_rows(self.config, imp.REPO)}
        for path in (imp.REPO / "Resources/Locale/en-US/_WF/Vehicles").glob("*.ftl"):
            lines = path.read_text(encoding="utf-8").splitlines()
            if any(line.startswith("rmc-") for line in lines):
                self.assertIn(path.relative_to(imp.REPO).as_posix(), rows, f"{path.name} has rmc-* keys but no manifest row")

    def test_manifest_block_lists_only_existing_files(self):
        block = imp.render_manifest_block(self.config, Path("/nonexistent"))
        self.assertIn("No source files have been imported yet.", block)
        self.assertTrue(re.search(r"\| `rmc` \| `e4627aee9e` \|", block))


if __name__ == "__main__":
    unittest.main()
