"""Tests for fix_namespaces.py. Run from this folder: python -m unittest test_fix_namespaces -v"""
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import fix_namespaces as fn  # noqa: E402

SHARED = "Content.Shared/_WF/Vehicles"
SERVER = "Content.Server/_WF/Vehicles"
CLIENT = "Content.Client/_WF/Vehicles"
TESTS = "Content.IntegrationTests/Tests/_WF/Vehicles"
PINNED_FILE = "Content.Client/_WF/Vehicles/Turret/GunSystem.AimOrigin.cs"


class FixNamespacesTest(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.addCleanup(self._dir.cleanup)
        self.repo = Path(self._dir.name)

    def write(self, rel: str, text: str, eol: str = "\n") -> None:
        path = self.repo / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(text.replace("\n", eol).encode("utf-8"))

    def read(self, rel: str) -> str:
        return (self.repo / rel).read_bytes().decode("utf-8")

    def run_fix(self, write: bool = True):
        return fn.fix(self.repo, write=write)

    # -- namespaces -------------------------------------------------------------------------------------------

    def test_namespace_follows_the_folder_at_any_depth(self):
        self.write(f"{SHARED}/Core/A.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class A;\n")
        self.write(f"{SHARED}/Grid/Components/B.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class B;\n")
        self.write(f"{SERVER}/Safety/C.cs", "namespace Content.Server._WF.Vehicles.Safety;\npublic sealed class C;\n")
        self.write(f"{TESTS}/D.cs", "namespace Content.IntegrationTests.Tests._WF.Vehicles;\npublic sealed class D;\n")
        self.write(f"{TESTS}/Sub/E.cs", "namespace Content.IntegrationTests.Tests._WF.Vehicles;\npublic sealed class E;\n")
        changed, problems = self.run_fix()
        self.assertEqual(problems, [])
        self.assertIn("namespace Content.Shared._WF.Vehicles.Core;", self.read(f"{SHARED}/Core/A.cs"))
        self.assertIn("namespace Content.Shared._WF.Vehicles.Grid.Components;", self.read(f"{SHARED}/Grid/Components/B.cs"))
        self.assertEqual(changed.count(f"{SERVER}/Safety/C.cs"), 0)
        self.assertIn("namespace Content.IntegrationTests.Tests._WF.Vehicles;", self.read(f"{TESTS}/D.cs"))
        self.assertIn("namespace Content.IntegrationTests.Tests._WF.Vehicles.Sub;", self.read(f"{TESTS}/Sub/E.cs"))

    def test_pinned_file_keeps_the_upstream_namespace_but_its_usings_are_repaired(self):
        self.write(f"{SHARED}/Weapons/G.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class G;\n")
        self.write(
            PINNED_FILE,
            "using Content.Shared._WF.Vehicles;\n\nnamespace Content.Client.Weapons.Ranged.Systems;\n\n"
            "public sealed partial class GunSystem\n{\n    private G _g = new();\n}\n",
        )
        self.run_fix()
        text = self.read(PINNED_FILE)
        self.assertIn("namespace Content.Client.Weapons.Ranged.Systems;", text)
        self.assertIn("using Content.Shared._WF.Vehicles.Weapons;", text)
        self.assertNotIn("using Content.Shared._WF.Vehicles;", text)

    def test_a_file_outside_the_module_namespaces_is_a_problem_unless_pinned(self):
        self.write(f"{CLIENT}/Turret/Odd.cs", "namespace Content.Client.Elsewhere;\npublic sealed partial class Odd;\n")
        changed, problems = self.run_fix()
        self.assertEqual(len(problems), 1)
        self.assertIn("Odd.cs", problems[0])

    def test_a_partial_type_split_across_folders_is_a_problem(self):
        self.write(f"{SHARED}/Core/P.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed partial class P;\n")
        self.write(f"{SHARED}/Grid/P.Part.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed partial class P;\n")
        _, problems = self.run_fix()
        self.assertEqual(len(problems), 1)
        self.assertIn("split across folders", problems[0])

    # -- usings -----------------------------------------------------------------------------------------------

    def test_usings_are_added_removed_and_sorted_in_a_module_file(self):
        self.write(f"{SHARED}/Core/Thing.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Thing;\n")
        self.write(f"{SHARED}/Hardpoint/Slot.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Slot;\n")
        self.write(f"{SHARED}/Grid/Mover.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Mover;\n")
        self.write(
            f"{SERVER}/Core/User.cs",
            "using System;\nusing Content.Server._Mono.Cleanup;\nusing Content.Shared._WF.Vehicles;\n"
            "using Content.Shared.Ghost;\nusing Robust.Shared.GameObjects;\n\n"
            "namespace Content.Server._WF.Vehicles;\n\npublic sealed class User\n{\n    Thing _t;\n    Slot _s;\n}\n",
        )
        self.run_fix()
        text = self.read(f"{SERVER}/Core/User.cs")
        usings = [line for line in text.split("\n") if line.startswith("using ")]
        self.assertEqual(
            usings,
            [
                "using System;",
                "using Content.Server._Mono.Cleanup;",
                "using Content.Shared._WF.Vehicles.Core;",
                "using Content.Shared._WF.Vehicles.Hardpoint;",
                "using Content.Shared.Ghost;",
                "using Robust.Shared.GameObjects;",
            ],
        )
        self.assertIn("namespace Content.Server._WF.Vehicles.Core;", text)

    def test_a_type_in_a_namespace_the_file_already_sees_needs_no_using(self):
        self.write(f"{SHARED}/Core/Thing.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Thing;\n")
        self.write(f"{SHARED}/Core/Other.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Other\n{\n    Thing _t;\n}\n")
        self.run_fix()
        text = self.read(f"{SHARED}/Core/Other.cs")
        self.assertNotIn("using", text)

    def test_member_names_comments_and_strings_do_not_count_as_uses(self):
        self.write(f"{SHARED}/Core/Thing.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Thing;\n")
        self.write(
            f"{SHARED}/Grid/Mover.cs",
            "using Content.Shared._WF.Vehicles;\n\nnamespace Content.Shared._WF.Vehicles;\n\n"
            "public sealed class Mover\n{\n    // Thing is only mentioned here\n    string s = \"Thing\";\n"
            "    object o => x.Thing;\n}\n",
        )
        self.run_fix()
        text = self.read(f"{SHARED}/Grid/Mover.cs")
        self.assertNotIn("using", text)
        self.assertIn("namespace Content.Shared._WF.Vehicles.Grid;", text)

    def test_an_extension_method_keeps_its_namespace_imported(self):
        self.write(
            f"{SHARED}/Core/Ext.cs",
            "namespace Content.Shared._WF.Vehicles;\npublic static class Ext\n{\n    public static int Twice(this int x) => x * 2;\n}\n",
        )
        self.write(
            f"{SHARED}/Grid/User.cs",
            "using Content.Shared._WF.Vehicles;\n\nnamespace Content.Shared._WF.Vehicles;\n\npublic sealed class User\n{\n    int y = 3.Twice();\n}\n",
        )
        self.run_fix()
        self.assertIn("using Content.Shared._WF.Vehicles.Core;", self.read(f"{SHARED}/Grid/User.cs"))

    def test_a_new_flat_file_is_picked_up_on_a_later_run(self):
        self.write(f"{SHARED}/Core/Thing.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Thing;\n")
        self.run_fix()
        self.assertEqual(self.run_fix(write=False)[0], [])
        self.write(
            f"{SHARED}/Weapons/Gun.cs",
            "using Content.Shared._WF.Vehicles;\n\nnamespace Content.Shared._WF.Vehicles;\n\npublic sealed class Gun\n{\n    Thing _t;\n}\n",
        )
        changed, _ = self.run_fix()
        self.assertEqual(changed, [f"{SHARED}/Weapons/Gun.cs"])
        text = self.read(f"{SHARED}/Weapons/Gun.cs")
        self.assertIn("using Content.Shared._WF.Vehicles.Core;", text)
        self.assertIn("namespace Content.Shared._WF.Vehicles.Weapons;", text)

    def test_second_run_changes_nothing(self):
        self.write(f"{SHARED}/Core/Thing.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Thing;\n")
        self.write(
            f"{SERVER}/Core/User.cs",
            "using Content.Shared._WF.Vehicles;\n\nnamespace Content.Server._WF.Vehicles;\n\npublic sealed class User\n{\n    Thing _t;\n}\n",
        )
        self.run_fix()
        before = {p: p.read_bytes() for p in self.repo.rglob("*.cs")}
        changed, problems = self.run_fix()
        self.assertEqual((changed, problems), ([], []))
        self.assertEqual(before, {p: p.read_bytes() for p in self.repo.rglob("*.cs")})

    # -- files outside the module -----------------------------------------------------------------------------

    def test_a_marked_hook_keeps_its_marker_on_every_replacement_line(self):
        self.write(f"{SHARED}/Core/Thing.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Thing;\n")
        self.write(f"{SHARED}/Hardpoint/Slot.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Slot;\n")
        self.write(
            "Content.Shared/Upstream/Hook.cs",
            "using System;\nusing Content.Shared._WF.Vehicles; // WOLFGATE(Vehicles)\nusing Content.Shared.Zed;\n"
            "using Alias = Content.Shared._WF.Vehicles.Thing; // WOLFGATE(Vehicles)\n\n"
            "namespace Content.Shared.Upstream;\n\npublic sealed class Hook\n{\n    Thing _t;\n    Slot _s;\n}\n",
            eol="\r\n",
        )
        self.run_fix()
        text = self.read("Content.Shared/Upstream/Hook.cs")
        self.assertIn("using Content.Shared._WF.Vehicles.Core; // WOLFGATE(Vehicles)\r\n", text)
        self.assertIn("using Content.Shared._WF.Vehicles.Hardpoint; // WOLFGATE(Vehicles)\r\n", text)
        self.assertIn("using Alias = Content.Shared._WF.Vehicles.Core.Thing; // WOLFGATE(Vehicles)\r\n", text)
        self.assertIn("namespace Content.Shared.Upstream;", text)
        order = [line.strip() for line in text.split("\r\n") if line.startswith("using ")]
        self.assertEqual(order[0], "using System;")
        self.assertEqual(order[-2], "using Content.Shared.Zed;")

    def test_only_files_that_name_a_module_namespace_are_touched(self):
        self.write(f"{SHARED}/Core/Thing.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Thing;\n")
        self.write("Content.Shared/Upstream/Plain.cs", "using System;\n\nnamespace Content.Shared.Upstream;\n\npublic sealed class Thing;\n")
        before = self.read("Content.Shared/Upstream/Plain.cs")
        self.run_fix()
        self.assertEqual(self.read("Content.Shared/Upstream/Plain.cs"), before)

    # -- line endings -----------------------------------------------------------------------------------------

    def test_line_endings_are_kept_and_never_doubled(self):
        self.write(f"{SHARED}/Core/Thing.cs", "namespace Content.Shared._WF.Vehicles;\npublic sealed class Thing;\n", eol="\r\n")
        self.write(f"{SHARED}/Grid/Lf.cs", "using Content.Shared._WF.Vehicles;\n\nnamespace Content.Shared._WF.Vehicles;\n\npublic sealed class Lf\n{\n    Thing _t;\n}\n")
        self.write(
            f"{SHARED}/Grid/Crlf.cs",
            "using Content.Shared._WF.Vehicles;\n\nnamespace Content.Shared._WF.Vehicles;\n\npublic sealed class Crlf\n{\n    Thing _t;\n}\n",
            eol="\r\n",
        )
        self.run_fix()
        crlf = self.read(f"{SHARED}/Grid/Crlf.cs")
        lf = self.read(f"{SHARED}/Grid/Lf.cs")
        self.assertNotIn("\r\r", crlf)
        self.assertEqual(crlf.count("\n"), crlf.count("\r\n"))
        self.assertNotIn("\r", lf)
        self.assertIn("using Content.Shared._WF.Vehicles.Core;\r\n", crlf)
        self.assertIn("using Content.Shared._WF.Vehicles.Core;\n", lf)

    def test_a_byte_order_mark_is_kept(self):
        path = self.repo / SHARED / "Core" / "Bom.cs"
        path.parent.mkdir(parents=True)
        path.write_bytes(b"\xef\xbb\xbfnamespace Content.Shared._WF.Vehicles;\npublic sealed class Bom;\n")
        self.run_fix()
        self.assertTrue(path.read_bytes().startswith(b"\xef\xbb\xbfnamespace Content.Shared._WF.Vehicles.Core;"))

    # -- XAML -------------------------------------------------------------------------------------------------

    def test_xaml_class_and_clr_namespace_follow_the_move(self):
        self.write(f"{CLIENT}/Ui/Button.cs", "namespace Content.Client._WF.Vehicles;\npublic sealed class MyButton;\n")
        self.write(
            f"{CLIENT}/Ui/Menu.xaml",
            '<DefaultWindow xmlns="https://spacestation14.io"\n'
            '    xmlns:controls="clr-namespace:Content.Client._WF.Vehicles"\n'
            '    xmlns:graphics="clr-namespace:Robust.Client.Graphics;assembly=Robust.Client"\n'
            '    x:Class="Content.Client._WF.Vehicles.Menu">\n    <controls:MyButton />\n</DefaultWindow>\n',
        )
        self.run_fix()
        xaml = self.read(f"{CLIENT}/Ui/Menu.xaml")
        self.assertIn('xmlns:controls="clr-namespace:Content.Client._WF.Vehicles.Ui"', xaml)
        self.assertIn('x:Class="Content.Client._WF.Vehicles.Ui.Menu"', xaml)
        self.assertIn("clr-namespace:Robust.Client.Graphics;assembly=Robust.Client", xaml)
        self.assertEqual(self.run_fix(write=False)[0], [])

    def test_a_xaml_prefix_that_would_need_two_namespaces_is_a_problem(self):
        self.write(f"{CLIENT}/Ui/A.cs", "namespace Content.Client._WF.Vehicles;\npublic sealed class A;\n")
        self.write(f"{CLIENT}/Grid/B.cs", "namespace Content.Client._WF.Vehicles;\npublic sealed class B;\n")
        self.write(
            f"{CLIENT}/Ui/Menu.xaml",
            '<Window xmlns:c="clr-namespace:Content.Client._WF.Vehicles" x:Class="Content.Client._WF.Vehicles.Menu">'
            "<c:A /><c:B /></Window>\n",
        )
        _, problems = self.run_fix()
        self.assertEqual(len(problems), 1)

    # -- helpers ----------------------------------------------------------------------------------------------

    def test_using_order_puts_system_first_and_underscore_before_letters(self):
        names = ["Robust.Shared.Map", "Content.Shared.Ghost", "System.Linq", "Content.Shared._WF.Vehicles", "System"]
        self.assertEqual(
            sorted(names, key=fn.using_key),
            ["System", "System.Linq", "Content.Shared._WF.Vehicles", "Content.Shared.Ghost", "Robust.Shared.Map"],
        )

    def test_chain_lists_the_enclosing_namespaces(self):
        self.assertEqual(fn.chain("A.B.C"), {"A", "A.B", "A.B.C"})
        self.assertEqual(fn.chain(None), set())


if __name__ == "__main__":
    unittest.main()
