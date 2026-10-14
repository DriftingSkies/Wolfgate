"""Tests for check_conventions.py. Run from this folder: python -m unittest test_check_conventions -v"""
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import check_conventions as cc  # noqa: E402

SHARED = "Content.Shared/_WF/Vehicles/Core"
LOCALE = "Resources/Locale/en-US/_WF/Vehicles"

CLEAN = (
    "using Robust.Shared.GameObjects;\n\nnamespace Content.Shared._WF.Vehicles.Core;\n\n"
    "/// <summary>A clean system.</summary>\npublic sealed partial class Clean : EntitySystem\n{\n"
    "    [Dependency] private SharedTransformSystem _transform = default!;\n\n"
    "    public string Text() => Loc.GetString(\"wf-clean\");\n}\n"
)


class CheckConventionsTest(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.addCleanup(self._dir.cleanup)
        self.repo = Path(self._dir.name)
        self.write(f"{SHARED}/Clean.cs", CLEAN)
        self.write(f"{LOCALE}/core.ftl", "wf-clean = Clean\n")

    def write(self, rel: str, text: str, eol: str = "\n") -> None:
        path = self.repo / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(text.replace("\n", eol).encode("utf-8"))

    def findings(self) -> list[str]:
        return cc.check(self.repo)

    def test_a_clean_module_has_no_findings(self):
        self.assertEqual(self.findings(), [])

    def test_a_licence_header_is_found(self):
        self.write(f"{SHARED}/Header.cs", "// Copyright (c) 2023 Somebody\nnamespace Content.Shared._WF.Vehicles.Core;\n")
        self.assertEqual(len(self.findings()), 1)
        self.assertIn("licence header", self.findings()[0])

    def test_a_prose_comment_about_licences_is_not_a_header(self):
        self.write("Tools/_WF/Vehicles/x.yml", "# Source roots and licences come from import_files.yml.\n")
        self.assertEqual(self.findings(), [])

    def test_readonly_dependency_is_found_but_a_plain_readonly_field_is_not(self):
        self.write(
            f"{SHARED}/Dep.cs",
            "namespace Content.Shared._WF.Vehicles.Core;\npublic sealed partial class Dep\n{\n"
            "    [Dependency] private readonly IFoo _foo = default!;\n    private readonly int _n = 1;\n}\n",
        )
        found = self.findings()
        self.assertEqual(len(found), 1)
        self.assertIn("readonly [Dependency]", found[0])

    def test_a_fluent_id_defined_twice_is_found(self):
        self.write(f"{LOCALE}/more.ftl", "wf-clean = Again\n")
        found = self.findings()
        self.assertEqual(len(found), 1)
        self.assertIn("defined 2 times", found[0])

    def test_a_module_id_clashing_with_another_locale_file_is_found(self):
        self.write("Resources/Locale/en-US/other.ftl", "wf-clean = Elsewhere\n")
        self.assertEqual(len(self.findings()), 1)

    def test_a_loc_key_nothing_defines_is_found_in_code_and_xaml(self):
        self.write(f"{SHARED}/Missing.cs", 'namespace X;\nclass M { string s = Loc.GetString("wf-missing"); }\n')
        self.write("Content.Client/_WF/Vehicles/Ui/W.xaml", '<Label Text="{Loc wf-missing-xaml}" />\n')
        found = self.findings()
        self.assertEqual(len(found), 2)
        self.assertTrue(all("not defined" in f for f in found))

    def test_an_interpolated_key_is_not_judged(self):
        self.write(f"{SHARED}/Dyn.cs", 'namespace X;\nclass M { string s = Loc.GetString($"wf-{kind}"); }\n')
        self.assertEqual(self.findings(), [])

    def test_todo_hack_and_fixme_are_found_in_code_and_yaml(self):
        self.write(f"{SHARED}/T.cs", "namespace X;\n// TODO: later\nclass T { }\n")
        self.write("Resources/Prototypes/_WF/Vehicles/a.yml", "- type: entity # RMC TODO: replace\n  id: A\n")
        self.assertEqual(len(self.findings()), 2)

    def test_commented_out_code_is_found_but_prose_and_kept_originals_follow_the_rule(self):
        self.write(f"{SHARED}/C.cs", "namespace X;\nclass C\n{\n    // var x = Foo(1);\n    // The mover keeps the place; it is simple.\n}\n")
        found = self.findings()
        self.assertEqual(len(found), 1)
        self.assertIn("commented-out code", found[0])

    def test_a_doubled_carriage_return_and_mixed_endings_are_found(self):
        path = self.repo / SHARED / "Cr.cs"
        path.write_bytes(b"namespace X;\r\r\nclass C { }\nint y;\r\n")
        found = self.findings()
        self.assertTrue(any("doubled carriage return" in f for f in found))
        self.assertTrue(any("mixed line endings" in f for f in found))


if __name__ == "__main__":
    unittest.main()
