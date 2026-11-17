using System;
using Content.Shared._WG.MappingMode;
using Content.Shared._WG.MappingMode.Export;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests._WG.MappingMode;

[TestFixture]
public sealed class WGMappingLibraryNamesTest
{
    private static readonly DateTime Stamp = new(2026, 10, 18, 14, 25, 1, DateTimeKind.Utc);

    [TestCase("  My Map_1-2 ", "My Map_1-2")]
    [TestCase("a", "a")]
    public void ValidMapNamesAreTrimmed(string raw, string expected)
    {
        Assert.That(WGMappingLibraryNames.TryNormalizeMapName(raw, out var name), Is.True);
        Assert.That(name, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("a/b")]
    [TestCase("..")]
    [TestCase("map.yml")]
    [TestCase("con")]
    [TestCase("LPT1")]
    [TestCase("Nul")]
    public void InvalidMapNamesAreRefused(string raw)
    {
        Assert.That(WGMappingLibraryNames.TryNormalizeMapName(raw, out _), Is.False);
    }

    [Test]
    public void MapNameLength()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGMappingLibraryNames.IsValidMapName(new string('a', WGMappingModeLimits.MaxNameLength)), Is.True);
            Assert.That(WGMappingLibraryNames.IsValidMapName(new string('a', WGMappingModeLimits.MaxNameLength + 1)), Is.False);
            Assert.That(WGMappingLibraryNames.TryNormalizeMapName(new string(' ', 500) + "a", out _), Is.False,
                "Very long input is refused before trimming.");
        });
    }

    [TestCase(1, WGMappingVersionKind.Manual, "v0001_20261018-142501_manual")]
    [TestCase(42, WGMappingVersionKind.Auto, "v0042_20261018-142501_auto")]
    [TestCase(12345, WGMappingVersionKind.Manual, "v12345_20261018-142501_manual")]
    [TestCase(7, WGMappingVersionKind.Upload, "v0007_20261018-142501_upload")]
    public void VersionFoldersRoundTrip(int number, WGMappingVersionKind kind, string folder)
    {
        var version = new WGMappingVersionName(number, Stamp, kind);
        Assert.That(version.Folder, Is.EqualTo(folder));
        Assert.That(WGMappingLibraryNames.TryParseVersion(folder, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.EqualTo(version));
            Assert.That(parsed.Stamp.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [TestCase("v001_20261018-142501_manual")]
    [TestCase("v0000_20261018-142501_manual")]
    [TestCase("x0001_20261018-142501_manual")]
    [TestCase("v0001_20261018-142501_other")]
    [TestCase("v0001_20261018-142501_Upload")]
    [TestCase("v0001_20261018-142501_uploaded")]
    [TestCase("v0001_20261318-142501_manual")]
    [TestCase("v0001_20261018-142501")]
    [TestCase("v0001_20261018-142501_manual_x")]
    [TestCase("v00a1_20261018-142501_manual")]
    [TestCase("v+001_20261018-142501_manual")]
    [TestCase("")]
    public void BadVersionFoldersAreIgnored(string folder)
    {
        Assert.That(WGMappingLibraryNames.TryParseVersion(folder, out _), Is.False);
    }

    [TestCase(-3)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(2)]
    [TestCase(10)]
    public void LevelFilesRoundTrip(int depth)
    {
        var file = WGMappingLibraryNames.LevelFile(depth);
        Assert.That(file, Is.EqualTo($"z{depth}.yml"));
        Assert.That(WGMappingLibraryNames.TryParseLevelFile(file, out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(depth));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(3)]
    public void GridAndVesselFilesRoundTrip(int depth)
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGMappingLibraryNames.GridFile(depth), Is.EqualTo($"z{depth}.grid.yml"));
            Assert.That(WGMappingLibraryNames.VesselFile(depth), Is.EqualTo($"z{depth}.vessel.yml"));
            Assert.That(WGMappingLibraryNames.TryParseGridFile(WGMappingLibraryNames.GridFile(depth), out var grid) &&
                        grid == depth, Is.True);
            Assert.That(WGMappingLibraryNames.TryParseVesselFile(WGMappingLibraryNames.VesselFile(depth), out var vessel) &&
                        vessel == depth, Is.True);

            // The three kinds never read as one another.
            Assert.That(WGMappingLibraryNames.TryParseLevelFile(WGMappingLibraryNames.GridFile(depth), out _), Is.False);
            Assert.That(WGMappingLibraryNames.TryParseLevelFile(WGMappingLibraryNames.VesselFile(depth), out _), Is.False);
            Assert.That(WGMappingLibraryNames.TryParseGridFile(WGMappingLibraryNames.LevelFile(depth), out _), Is.False);
            Assert.That(WGMappingLibraryNames.TryParseGridFile(WGMappingLibraryNames.VesselFile(depth), out _), Is.False);
        });
    }

    [Test]
    public void DownloadNames()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGMappingLibraryNames.DownloadMapFile("test"), Is.EqualTo("test.yml"));
            Assert.That(WGMappingLibraryNames.DownloadLevelFile("test", 1), Is.EqualTo("test_z1.yml"));
            Assert.That(WGMappingLibraryNames.DownloadLevelFile("test", -1), Is.EqualTo("test_z-1.yml"));
            Assert.That(WGMappingLibraryNames.DownloadVesselFile("test"), Is.EqualTo("test.vessel.yml"));
            Assert.That(WGMappingLibraryNames.IsDownloadFile("test", "test_z1.yml"), Is.True);
            Assert.That(WGMappingLibraryNames.IsDownloadFile("test", "test.grid.yml"), Is.False);
            Assert.That(WGMappingLibraryNames.DownloadGridFile("Old Outpost 2"), Is.EqualTo("old_outpost_2.yml"), "The grid export follows the ship grid's naming.");
            Assert.That(WGMappingLibraryNames.ExportFile("Old Outpost 2", WGMappingExportKind.Grid), Is.EqualTo("old_outpost_2.yml"));
            Assert.That(WGMappingLibraryNames.ExportFile("Old Outpost 2", WGMappingExportKind.Ship), Is.EqualTo("Old Outpost 2.zip"));
            Assert.That(WGMappingLibraryNames.ExportFile("Old Outpost 2", WGMappingExportKind.Map), Is.EqualTo("Old Outpost 2.zip"));
            Assert.That(WGMappingLibraryNames.IsDownloadFile("Old Outpost 2", "old_outpost_2.yml"), Is.True, "The grid export's name doesn't start with the map name.");
            Assert.That(WGMappingLibraryNames.IsDownloadFile("Old Outpost 2", "old_outpost.yml"), Is.False);
            Assert.That(WGMappingLibraryNames.IsDownloadFile("CON", "CON.yml"), Is.False, "Not a valid map name.");
        });
    }

    [TestCase("z.yml")]
    [TestCase("z01.yml")]
    [TestCase("z-0.yml")]
    [TestCase("z+1.yml")]
    [TestCase("z1.yaml")]
    [TestCase("zz.yml")]
    [TestCase("../z0.yml")]
    [TestCase("z0.yml/..")]
    [TestCase("Z0.yml")]
    public void BadLevelFilesAreRefused(string file)
    {
        Assert.That(WGMappingLibraryNames.TryParseLevelFile(file, out _), Is.False);
    }

    [TestCase("Outpost.yml", "Outpost")]
    [TestCase(@"C:\Users\me\Maps\My Base.YML", "My Base")]
    [TestCase("/home/me/maps/dock-7_b.yml", "dock-7_b")]
    [TestCase("  spaced  .yml", "spaced")]
    [TestCase("bad.name!.yml", "badname")]
    [TestCase("map.yaml", "mapyaml")]
    [TestCase("con.yml", WGMappingLibraryNames.UploadFallbackName)]
    [TestCase("!!!.yml", WGMappingLibraryNames.UploadFallbackName)]
    [TestCase(".yml", WGMappingLibraryNames.UploadFallbackName)]
    [TestCase("folder/", WGMappingLibraryNames.UploadFallbackName)]
    [TestCase("", WGMappingLibraryNames.UploadFallbackName)]
    [TestCase(null, WGMappingLibraryNames.UploadFallbackName)]
    public void UploadNameFromFile(string file, string expected)
    {
        Assert.That(WGMappingLibraryNames.UploadNameFromFile(file), Is.EqualTo(expected));
    }

    [Test]
    public void UploadNameIsCutToLength()
    {
        var name = WGMappingLibraryNames.UploadNameFromFile(new string('a', 100) + ".yml");
        Assert.Multiple(() =>
        {
            Assert.That(name, Has.Length.EqualTo(WGMappingModeLimits.MaxNameLength));
            Assert.That(WGMappingLibraryNames.IsValidMapName(name), Is.True);
        });
    }

    [Test]
    public void Paths()
    {
        var user = new NetUserId(new Guid("01234567-89ab-cdef-0123-456789abcdef"));
        var version = new WGMappingVersionName(3, Stamp, WGMappingVersionKind.Auto).Folder;
        Assert.Multiple(() =>
        {
            Assert.That(WGMappingLibraryNames.LevelPath(user, "Outpost", version, -1).ToString(),
                Is.EqualTo($"/Mapping/Library/{user}/Outpost/v0003_20261018-142501_auto/z-1.yml"));
            Assert.That(WGMappingLibraryNames.DownloadPath("Outpost", version, "z0.yml").ToString(),
                Is.EqualTo("/Mapping/Outpost/v0003_20261018-142501_auto/z0.yml"));
        });
    }
}
