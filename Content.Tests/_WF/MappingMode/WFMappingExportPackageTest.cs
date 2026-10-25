using System.Collections.Generic;
using System.Linq;
using System.Text;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingMode.Export;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Export;
using Content.Shared._WF.MappingMode.Issues;
using NUnit.Framework;

namespace Content.Tests._WF.MappingMode;

/// <summary>
/// The pure parts of the export: entry paths that mirror the repository, the map prototype text, the README, the zip
/// round trip, the ship details YAML and sanitizing, and the vessel fields the details override.
/// </summary>
[TestFixture]
public sealed class WFMappingExportPackageTest
{
    [Test]
    public void EntryPathsMirrorTheRepository()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFMappingExportPackage.ShipGridEntry("bob_s_barge"), Is.EqualTo("Resources/SharedMaps/_WF/Community/bob_s_barge.yml"));
            Assert.That(WFMappingExportPackage.PrototypeEntry("bob_s_barge"), Is.EqualTo("Resources/Prototypes/_WF/Community/bob_s_barge.yml"));
            Assert.That(WFMappingExportPackage.MapLevelEntry("outpost", 0, true), Is.EqualTo("Resources/Maps/_WF/Community/outpost.yml"));
            Assert.That(WFMappingExportPackage.MapLevelEntry("outpost", -1, false), Is.EqualTo("Resources/Maps/_WF/Community/outpost/z-1.yml"));
            Assert.That(WFMappingExportPackage.MapLevelResource("outpost", 2, false), Is.EqualTo("/Maps/_WF/Community/outpost/z2.yml"));
            Assert.That(WFMappingLibraryNames.DownloadZipFile("Bob's Barge"), Is.EqualTo("Bob's Barge.zip"));
            Assert.That(WFMappingLibraryNames.IsDownloadFile("Bobs Barge", "Bobs Barge.zip"), Is.True);
            Assert.That(WFMappingLibraryNames.IsDownloadFile("Bobs Barge", "Other.zip"), Is.False);
            Assert.That(WFMappingLibraryNames.TryParseScanFile("z0.scan.yml", out var depth), Is.True);
            Assert.That(depth, Is.Zero);
            Assert.That(WFMappingLibraryNames.TryParseVesselFile("z0.scan.yml", out _), Is.False);
        });
    }

    [Test]
    public void MapPrototypeIsAGameMapForOneLevelAndAZMapForMore()
    {
        var single = WFMappingExportPackage.BuildMapPrototype("Old Outpost", "old_outpost", new[] { 0 }, new[] { "header" });
        Assert.Multiple(() =>
        {
            Assert.That(single, Does.StartWith("# header\n"));
            Assert.That(single, Does.Contain("- type: gameMap\n  id: WFOldOutpost\n"));
            Assert.That(single, Does.Contain("mapPath: /Maps/_WF/Community/old_outpost.yml"));
            Assert.That(single, Does.Contain("stationProto:"));
            Assert.That(single, Does.Not.Contain("zMap"));
        });

        var stacked = WFMappingExportPackage.BuildMapPrototype("Old Outpost", "old_outpost", new[] { -1, 0, 1 }, new string[0]);
        Assert.Multiple(() =>
        {
            Assert.That(stacked, Does.Contain("- type: zMap\n  id: WFOldOutpost\n"));
            Assert.That(stacked, Does.Contain("ground level (depth 0) is index 1"));
            var maps = stacked.Split('\n').Where(line => line.StartsWith("  - /Maps/")).ToList();
            Assert.That(maps, Is.EqualTo(new[]
            {
                "  - /Maps/_WF/Community/old_outpost/z-1.yml",
                "  - /Maps/_WF/Community/old_outpost/z0.yml",
                "  - /Maps/_WF/Community/old_outpost/z1.yml",
            }), "Levels are listed bottom-up.");
            Assert.That(stacked, Does.Not.Contain("gameMap"));
        });
    }

    [Test]
    public void ReadmeListsIssuesBySeverity()
    {
        var issues = new List<WFMappingExportIssueLine>
        {
            new(WFMappingIssueSeverity.Note, "a note", "note hint"),
            new(WFMappingIssueSeverity.Error, "an error", "error hint"),
            new(WFMappingIssueSeverity.Warning, "a warning", string.Empty),
        };
        var text = WFMappingExportPackage.BuildReadme(new[] { "intro" }, new[] { "file one" }, new[] { "check one" },
            "Issues:", issues, "none", severity => severity + ":");
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("intro\n\n  file one\n\n- check one\n\nIssues:\n"));
            Assert.That(text.IndexOf("Error:"), Is.LessThan(text.IndexOf("Warning:")));
            Assert.That(text.IndexOf("Warning:"), Is.LessThan(text.IndexOf("Note:")));
            Assert.That(text, Does.Contain("  - an error\n      error hint\n"));
            Assert.That(text, Does.Contain("  - a warning\n\nNote:"), "A warning with no hint takes one line.");
            Assert.That(text, Does.Not.Contain("none"));
        });

        var empty = WFMappingExportPackage.BuildReadme(new[] { "intro" }, new string[0], new string[0], "Issues:", new List<WFMappingExportIssueLine>(), "none", s => s.ToString());
        Assert.That(empty, Does.EndWith("Issues:\n  none\n"));
        var unverified = WFMappingExportPackage.BuildReadme(new[] { "intro" }, new string[0], new string[0], "Unchecked.", null, "none", s => s.ToString());
        Assert.That(unverified, Does.EndWith("Unchecked.\n  none\n"));
    }

    [Test]
    public void ZipRoundTrips()
    {
        var entries = new List<WFMappingExportEntry>
        {
            new("Resources/SharedMaps/_WF/Community/a.yml", Encoding.UTF8.GetBytes("meta:\n  format: 7\n")),
            new("README.txt", "hello\n"),
        };
        var zip = WFMappingExportPackage.Zip(entries);
        Assert.That(zip, Has.Length.GreaterThan(4));
        Assert.That(zip.Take(2), Is.EqualTo(new byte[] { 0x50, 0x4b }), "A zip starts with PK.");

        var back = WFMappingExportPackage.Unzip(zip);
        Assert.That(back.Select(e => e.Path), Is.EqualTo(entries.Select(e => e.Path)));
        Assert.That(back[0].Data, Is.EqualTo(entries[0].Data));
        Assert.That(Encoding.UTF8.GetString(back[1].Data), Is.EqualTo("hello\n"));
    }

    [Test]
    public void ShipDetailsRoundTripAndSanitize()
    {
        var details = new WFMappingShipDetails
        {
            Name = "  Bob's Barge ",
            Description = "Line one\r\nLine: two 'quoted' \"double\"\u0001",
            Price = 12_500,
            Category = VesselSize.Medium,
            Classes = new List<VesselClass> { VesselClass.Cargo, VesselClass.Cargo, VesselClass.All, VesselClass.Salvage },
            Group = ShipyardConsoleUiKey.Security,
            AutoEngines = false,
            Engines = new List<VesselEngine> { VesselEngine.Plasma, VesselEngine.All },
            Access = "Command",
            RequireCrew = true,
        };

        var clean = WFMappingExportLimits.Sanitize(details);
        Assert.Multiple(() =>
        {
            Assert.That(clean.Name, Is.Empty, "An apostrophe isn't in the name alphabet, so the name falls back to the map's.");
            Assert.That(clean.Description, Is.EqualTo("Line one\nLine: two 'quoted' \"double\""));
            Assert.That(clean.Classes, Is.EqualTo(new[] { VesselClass.Cargo, VesselClass.Salvage }));
            Assert.That(clean.Engines, Is.EqualTo(new[] { VesselEngine.Plasma }));
            Assert.That(clean.Price, Is.EqualTo(12_500));
        });

        details.Name = "Bobs Barge";
        var text = WFMappingShipDetailsYaml.Write(WFMappingExportLimits.Sanitize(details));
        var read = WFMappingShipDetailsYaml.Read(text);
        Assert.Multiple(() =>
        {
            Assert.That(read.Name, Is.EqualTo("Bobs Barge"));
            Assert.That(read.Description, Is.EqualTo("Line one\nLine: two 'quoted' \"double\""));
            Assert.That(read.Price, Is.EqualTo(12_500));
            Assert.That(read.Category, Is.EqualTo(VesselSize.Medium));
            Assert.That(read.Classes, Is.EqualTo(new[] { VesselClass.Cargo, VesselClass.Salvage }));
            Assert.That(read.Group, Is.EqualTo(ShipyardConsoleUiKey.Security));
            Assert.That(read.AutoEngines, Is.False);
            Assert.That(read.Engines, Is.EqualTo(new[] { VesselEngine.Plasma }));
            Assert.That(read.Access, Is.EqualTo("Command"));
            Assert.That(read.RequireCrew, Is.True);
        });

        var defaults = WFMappingShipDetailsYaml.Read("not: [yaml");
        Assert.That(defaults.AutoEngines, Is.True);
        Assert.That(defaults.Price, Is.Zero);

        var limits = WFMappingExportLimits.Sanitize(new WFMappingShipDetails
        {
            Description = new string('x', WFMappingExportLimits.MaxDescriptionLength + 50),
            Price = WFMappingExportLimits.MaxPrice + 1,
            Access = "no spaces allowed",
            Category = (VesselSize) 200,
        });
        Assert.Multiple(() =>
        {
            Assert.That(limits.Description, Has.Length.EqualTo(WFMappingExportLimits.MaxDescriptionLength));
            Assert.That(limits.Price, Is.EqualTo(WFMappingExportLimits.MaxPrice));
            Assert.That(limits.Access, Is.Empty);
            Assert.That(limits.Category, Is.EqualTo(VesselSize.All));
        });
    }

    [Test]
    public void DetailsOverrideTheGuesses()
    {
        var scan = new WFMappingVesselScan { Tiles = 120, Appraisal = 9000, Mining = true };
        scan.Engines.Add(VesselEngine.AME);

        var guessed = WFMappingVesselYaml.Resolve("Bobs Barge", scan, null, "placeholder");
        Assert.Multiple(() =>
        {
            Assert.That(guessed.Name, Is.EqualTo("Bobs Barge"));
            Assert.That(guessed.Id, Is.EqualTo("WFBobsBarge"));
            Assert.That(guessed.FileName, Is.EqualTo("bobs_barge"));
            Assert.That(guessed.Description, Is.EqualTo("placeholder"));
            Assert.That(guessed.Price, Is.EqualTo(WFMappingShipNaming.PriceFor(9000)));
            Assert.That(guessed.Category, Is.EqualTo(VesselSize.Small));
            Assert.That(guessed.Classes, Is.EqualTo(new[] { VesselClass.Salvage }));
            Assert.That(guessed.Engines, Is.EqualTo(new[] { VesselEngine.AME }));
            Assert.That(guessed.Guessed, Is.EqualTo(new[] { "description", "price", "category", "class", "engine" }));
        });

        var details = new WFMappingShipDetails
        {
            Name = "Barge Two",
            Description = "Mine.",
            Price = 20_000,
            Category = VesselSize.Large,
            Classes = new List<VesselClass> { VesselClass.Cargo },
            Group = ShipyardConsoleUiKey.Scrap,
            AutoEngines = false,
            Engines = new List<VesselEngine> { VesselEngine.Solar },
            Access = "Command",
            RequireCrew = true,
        };
        var fields = WFMappingVesselYaml.Resolve("Bobs Barge", scan, details, "placeholder");
        Assert.Multiple(() =>
        {
            Assert.That(fields.Name, Is.EqualTo("Barge Two"));
            Assert.That(fields.Id, Is.EqualTo("WFBargeTwo"));
            Assert.That(fields.Guessed, Is.Empty);
            Assert.That(fields.Engines, Is.EqualTo(new[] { VesselEngine.Solar }));
        });

        var text = WFMappingVesselYaml.Build(fields, scan, new[] { "header" });
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("  id: WFBargeTwo\n"));
            Assert.That(text, Does.Contain("  name: 'Barge Two'\n"));
            Assert.That(text, Does.Contain("  description: 'Mine.'\n"));
            Assert.That(text, Does.Contain("  price: 20000 # appraised at 9000\n"));
            Assert.That(text, Does.Contain("  category: Large # 120 tiles\n"));
            Assert.That(text, Does.Contain("  group: Scrap\n"));
            Assert.That(text, Does.Contain("  access: Command\n"));
            Assert.That(text, Does.Contain("  requireCrew: true\n"));
            Assert.That(text, Does.Contain("  shuttlePath: /SharedMaps/_WF/Community/barge_two.yml\n"));
            Assert.That(text, Does.Contain("  class:\n  - Cargo\n"));
            Assert.That(text, Does.Contain("  engine:\n  - Solar\n"));
        });
    }

    [Test]
    public void ScanRoundTrips()
    {
        var scan = new WFMappingVesselScan { Tiles = 33, Appraisal = 1234.5, CargoPallet = true, MountedGuns = true };
        scan.Engines.Add(VesselEngine.Uranium);
        scan.Engines.Add(VesselEngine.AME);
        var read = WFMappingVesselScan.Read(scan.Write());
        Assert.That(read, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(read!.Tiles, Is.EqualTo(33));
            Assert.That(read.Appraisal, Is.EqualTo(1234.5));
            Assert.That(read.CargoPallet, Is.True);
            Assert.That(read.Mining, Is.False);
            Assert.That(read.MountedGuns, Is.True);
            Assert.That(read.Engines, Is.EquivalentTo(new[] { VesselEngine.AME, VesselEngine.Uranium }));
        });
        Assert.That(WFMappingVesselScan.Read("- a list"), Is.Null);
    }

    [Test]
    public void SharedNamingMatchesTheGenerator()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFMappingShipNaming.VesselId("my cool-ship 2"), Is.EqualTo(WFMappingVesselYaml.VesselId("my cool-ship 2")));
            Assert.That(WFMappingShipNaming.FileName("MMC Dredger"), Is.EqualTo("mmc_dredger"));
            Assert.That(WFMappingShipNaming.PriceFor(7999), Is.EqualTo(10000));
            Assert.That(WFMappingShipNaming.PriceFloor(7999.2), Is.EqualTo(8000));
            Assert.That(WFMappingShipNaming.PriceFloor(0), Is.Zero);
            Assert.That(WFMappingShipNaming.SizeFor(500), Is.EqualTo(VesselSize.Large));
        });
    }
}
