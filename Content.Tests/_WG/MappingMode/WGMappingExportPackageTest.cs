using System.Collections.Generic;
using System.Linq;
using System.Text;
using Content.Server._WG.MappingMode;
using Content.Server._WG.MappingMode.Export;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.MappingMode;
using Content.Shared._WG.MappingMode.Export;
using Content.Shared._WG.MappingMode.Issues;
using NUnit.Framework;

namespace Content.Tests._WG.MappingMode;

/// <summary>
/// The pure parts of the export: entry paths that mirror the repository, the map prototype text, the README, the zip
/// round trip, the ship details YAML and sanitizing, and the vessel fields the details override.
/// </summary>
[TestFixture]
public sealed class WGMappingExportPackageTest
{
    [Test]
    public void EntryPathsMirrorTheRepository()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGMappingExportPackage.ShipGridEntry("bob_s_barge"), Is.EqualTo("Resources/SharedMaps/_WG/Community/bob_s_barge.yml"));
            Assert.That(WGMappingExportPackage.PrototypeEntry("bob_s_barge"), Is.EqualTo("Resources/Prototypes/_WG/Community/bob_s_barge.yml"));
            Assert.That(WGMappingExportPackage.MapLevelEntry("outpost", 0, true), Is.EqualTo("Resources/Maps/_WG/Community/outpost.yml"));
            Assert.That(WGMappingExportPackage.MapLevelEntry("outpost", -1, false), Is.EqualTo("Resources/Maps/_WG/Community/outpost/z-1.yml"));
            Assert.That(WGMappingExportPackage.MapLevelResource("outpost", 2, false), Is.EqualTo("/Maps/_WG/Community/outpost/z2.yml"));
            Assert.That(WGMappingLibraryNames.DownloadZipFile("Bob's Barge"), Is.EqualTo("Bob's Barge.zip"));
            Assert.That(WGMappingLibraryNames.IsDownloadFile("Bobs Barge", "Bobs Barge.zip"), Is.True);
            Assert.That(WGMappingLibraryNames.IsDownloadFile("Bobs Barge", "Other.zip"), Is.False);
            Assert.That(WGMappingLibraryNames.TryParseScanFile("z0.scan.yml", out var depth), Is.True);
            Assert.That(depth, Is.Zero);
            Assert.That(WGMappingLibraryNames.TryParseVesselFile("z0.scan.yml", out _), Is.False);
        });
    }

    [Test]
    public void MapPrototypeIsAGameMapForOneLevelAndAZMapForMore()
    {
        var single = WGMappingExportPackage.BuildMapPrototype("Old Outpost", "old_outpost", new[] { 0 }, new[] { "header" });
        Assert.Multiple(() =>
        {
            Assert.That(single, Does.StartWith("# header\n"));
            Assert.That(single, Does.Contain("- type: gameMap\n  id: WGOldOutpost\n"));
            Assert.That(single, Does.Contain("mapPath: /Maps/_WG/Community/old_outpost.yml"));
            Assert.That(single, Does.Contain("stationProto:"));
            Assert.That(single, Does.Not.Contain("zMap"));
        });

        var stacked = WGMappingExportPackage.BuildMapPrototype("Old Outpost", "old_outpost", new[] { -1, 0, 1 }, new string[0]);
        Assert.Multiple(() =>
        {
            Assert.That(stacked, Does.Contain("- type: zMap\n  id: WGOldOutpost\n"));
            Assert.That(stacked, Does.Contain("ground level (depth 0) is index 1"));
            var maps = stacked.Split('\n').Where(line => line.StartsWith("  - /Maps/")).ToList();
            Assert.That(maps, Is.EqualTo(new[]
            {
                "  - /Maps/_WG/Community/old_outpost/z-1.yml",
                "  - /Maps/_WG/Community/old_outpost/z0.yml",
                "  - /Maps/_WG/Community/old_outpost/z1.yml",
            }), "Levels are listed bottom-up.");
            Assert.That(stacked, Does.Not.Contain("gameMap"));
        });
    }

    [Test]
    public void ReadmeListsIssuesBySeverity()
    {
        var issues = new List<WGMappingExportIssueLine>
        {
            new(WGMappingIssueSeverity.Note, "a note", "note hint"),
            new(WGMappingIssueSeverity.Error, "an error", "error hint"),
            new(WGMappingIssueSeverity.Warning, "a warning", string.Empty),
        };
        var text = WGMappingExportPackage.BuildReadme(new[] { "intro" }, new[] { "file one" }, new[] { "check one" },
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

        var empty = WGMappingExportPackage.BuildReadme(new[] { "intro" }, new string[0], new string[0], "Issues:", new List<WGMappingExportIssueLine>(), "none", s => s.ToString());
        Assert.That(empty, Does.EndWith("Issues:\n  none\n"));
        var unverified = WGMappingExportPackage.BuildReadme(new[] { "intro" }, new string[0], new string[0], "Unchecked.", null, "none", s => s.ToString());
        Assert.That(unverified, Does.EndWith("Unchecked.\n  none\n"));
    }

    [Test]
    public void ZipRoundTrips()
    {
        var entries = new List<WGMappingExportEntry>
        {
            new("Resources/SharedMaps/_WG/Community/a.yml", Encoding.UTF8.GetBytes("meta:\n  format: 7\n")),
            new("README.txt", "hello\n"),
        };
        var zip = WGMappingExportPackage.Zip(entries);
        Assert.That(zip, Has.Length.GreaterThan(4));
        Assert.That(zip.Take(2), Is.EqualTo(new byte[] { 0x50, 0x4b }), "A zip starts with PK.");

        var back = WGMappingExportPackage.Unzip(zip);
        Assert.That(back.Select(e => e.Path), Is.EqualTo(entries.Select(e => e.Path)));
        Assert.That(back[0].Data, Is.EqualTo(entries[0].Data));
        Assert.That(Encoding.UTF8.GetString(back[1].Data), Is.EqualTo("hello\n"));
    }

    [Test]
    public void ShipDetailsRoundTripAndSanitize()
    {
        var details = new WGMappingShipDetails
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

        var clean = WGMappingExportLimits.Sanitize(details);
        Assert.Multiple(() =>
        {
            Assert.That(clean.Name, Is.Empty, "An apostrophe isn't in the name alphabet, so the name falls back to the map's.");
            Assert.That(clean.Description, Is.EqualTo("Line one\nLine: two 'quoted' \"double\""));
            Assert.That(clean.Classes, Is.EqualTo(new[] { VesselClass.Cargo, VesselClass.Salvage }));
            Assert.That(clean.Engines, Is.EqualTo(new[] { VesselEngine.Plasma }));
            Assert.That(clean.Price, Is.EqualTo(12_500));
        });

        details.Name = "Bobs Barge";
        var text = WGMappingShipDetailsYaml.Write(WGMappingExportLimits.Sanitize(details));
        var read = WGMappingShipDetailsYaml.Read(text);
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

        var defaults = WGMappingShipDetailsYaml.Read("not: [yaml");
        Assert.That(defaults.AutoEngines, Is.True);
        Assert.That(defaults.Price, Is.Zero);

        var limits = WGMappingExportLimits.Sanitize(new WGMappingShipDetails
        {
            Description = new string('x', WGMappingExportLimits.MaxDescriptionLength + 50),
            Price = WGMappingExportLimits.MaxPrice + 1,
            Access = "no spaces allowed",
            Category = (VesselSize) 200,
        });
        Assert.Multiple(() =>
        {
            Assert.That(limits.Description, Has.Length.EqualTo(WGMappingExportLimits.MaxDescriptionLength));
            Assert.That(limits.Price, Is.EqualTo(WGMappingExportLimits.MaxPrice));
            Assert.That(limits.Access, Is.Empty);
            Assert.That(limits.Category, Is.EqualTo(VesselSize.All));
        });
    }

    [Test]
    public void DetailsOverrideTheGuesses()
    {
        var scan = new WGMappingVesselScan { Tiles = 120, Appraisal = 9000, Mining = true };
        scan.Engines.Add(VesselEngine.AME);

        var guessed = WGMappingVesselYaml.Resolve("Bobs Barge", scan, null, "placeholder");
        Assert.Multiple(() =>
        {
            Assert.That(guessed.Name, Is.EqualTo("Bobs Barge"));
            Assert.That(guessed.Id, Is.EqualTo("WGBobsBarge"));
            Assert.That(guessed.FileName, Is.EqualTo("bobs_barge"));
            Assert.That(guessed.Description, Is.EqualTo("placeholder"));
            Assert.That(guessed.Price, Is.EqualTo(WGMappingShipNaming.PriceFor(9000)));
            Assert.That(guessed.Category, Is.EqualTo(VesselSize.Small));
            Assert.That(guessed.Classes, Is.EqualTo(new[] { VesselClass.Salvage }));
            Assert.That(guessed.Engines, Is.EqualTo(new[] { VesselEngine.AME }));
            Assert.That(guessed.Guessed, Is.EqualTo(new[] { "description", "price", "category", "class", "engine" }));
        });

        var details = new WGMappingShipDetails
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
        var fields = WGMappingVesselYaml.Resolve("Bobs Barge", scan, details, "placeholder");
        Assert.Multiple(() =>
        {
            Assert.That(fields.Name, Is.EqualTo("Barge Two"));
            Assert.That(fields.Id, Is.EqualTo("WGBargeTwo"));
            Assert.That(fields.Guessed, Is.Empty);
            Assert.That(fields.Engines, Is.EqualTo(new[] { VesselEngine.Solar }));
        });

        var text = WGMappingVesselYaml.Build(fields, scan, new[] { "header" });
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("  id: WGBargeTwo\n"));
            Assert.That(text, Does.Contain("  name: 'Barge Two'\n"));
            Assert.That(text, Does.Contain("  description: 'Mine.'\n"));
            Assert.That(text, Does.Contain("  price: 20000 # appraised at 9000\n"));
            Assert.That(text, Does.Contain("  category: Large # 120 tiles\n"));
            Assert.That(text, Does.Contain("  group: Scrap\n"));
            Assert.That(text, Does.Contain("  access: Command\n"));
            Assert.That(text, Does.Contain("  requireCrew: true\n"));
            Assert.That(text, Does.Contain("  shuttlePath: /SharedMaps/_WG/Community/barge_two.yml\n"));
            Assert.That(text, Does.Contain("  class:\n  - Cargo\n"));
            Assert.That(text, Does.Contain("  engine:\n  - Solar\n"));
        });
    }

    [Test]
    public void ScanRoundTrips()
    {
        var scan = new WGMappingVesselScan { Tiles = 33, Appraisal = 1234.5, CargoPallet = true, MountedGuns = true };
        scan.Engines.Add(VesselEngine.Uranium);
        scan.Engines.Add(VesselEngine.AME);
        var read = WGMappingVesselScan.Read(scan.Write());
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
        Assert.That(WGMappingVesselScan.Read("- a list"), Is.Null);
    }

    [Test]
    public void SharedNamingMatchesTheGenerator()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGMappingShipNaming.VesselId("my cool-ship 2"), Is.EqualTo(WGMappingVesselYaml.VesselId("my cool-ship 2")));
            Assert.That(WGMappingShipNaming.FileName("MMC Dredger"), Is.EqualTo("mmc_dredger"));
            Assert.That(WGMappingShipNaming.PriceFor(7999), Is.EqualTo(10000));
            Assert.That(WGMappingShipNaming.PriceFloor(7999.2), Is.EqualTo(8000));
            Assert.That(WGMappingShipNaming.PriceFloor(0), Is.Zero);
            Assert.That(WGMappingShipNaming.SizeFor(500), Is.EqualTo(VesselSize.Large));
        });
    }
}
