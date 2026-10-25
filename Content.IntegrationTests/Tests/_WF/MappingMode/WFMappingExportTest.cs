#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._WF.MappingMode;
using Content.IntegrationTests.Pair;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingMode.Export;
using Content.Server._WF.MappingTools;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Export;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// The export wizard's server side: a ship session saved and exported arrives on the client as one zip holding the grid,
/// the vessel prototype with the mapper's details and a README; the details persist in the version, ride into the next
/// save and reach the library listing; an error in the check refuses the ship export; a map of two levels exports its
/// level files and a zMap, one level a gameMap; Go to moves the observer; a grid export is the bare grid file, refused
/// for a version of two grids.
/// </summary>
[TestFixture]
public sealed class WFMappingExportTest
{
    private const string ShipName = "Export Ship";
    private const string LevelsName = "Export Levels";
    private const string GridName = "Export Grid";

    /// <summary>The parts that clear every ship error, placed along the grid's middle row.</summary>
    private static readonly string[] Parts =
    {
        "ComputerShuttle", "AirlockShuttle", "Thruster", "Gyroscope", "GeneratorBasic", "APCBasic", "SMESBasic",
        "SpawnPointLatejoin", "GasPipeStraight",
    };

    private static Entity<MapGridComponent> MakeGrid(IEntityManager entMan, MapSystem mapSys,
        ITileDefinitionManager tileDefs, EntityUid map, int width, int height)
    {
        var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(map).MapId);
        var plating = new Tile(tileDefs["Plating"].TileId);
        var cells = new List<(Vector2i, Tile)>();
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                cells.Add((new Vector2i(x, y), plating));
            }
        }

        mapSys.SetTiles(grid, grid.Comp, cells);
        return grid;
    }

    private static byte[] Read(IResourceManager res, ResPath path)
    {
        using var stream = res.UserData.OpenRead(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Text(List<WFMappingExportEntry> entries, string path)
    {
        var entry = entries.FirstOrDefault(e => e.Path == path);
        Assert.That(entry.Path, Is.EqualTo(path), $"The zip should hold {path}; it holds {string.Join(", ", entries.Select(e => e.Path))}");
        return Encoding.UTF8.GetString(entry.Data);
    }

    /// <summary>Saves through the network as the Save tab does and waits for the version.</summary>
    private static async Task<WFMappingLibrarySystem.LibraryVersion> SaveAndWait(TestPair pair, string mapName, int number)
    {
        var library = pair.Server.EntMan.System<WFMappingLibrarySystem>();
        var clientSys = pair.Client.EntMan.System<WFMappingClientSystem>();
        var player = pair.Player!;
        await pair.Server.WaitPost(() => RaiseAs(pair.Server.EntMan, player, new WFMappingSaveRequest(string.Empty)));
        WFMappingLibrarySystem.LibraryVersion? version = null;
        for (var i = 0; i < 100 && version == null; i++)
        {
            await pair.RunTicksSync(1);
            await pair.Server.WaitPost(() =>
            {
                if (library.TryFindVersion(player.UserId, mapName, null, out _, out var found) && found.Name.Number == number)
                    version = found;
            });
        }

        Assert.That(version, Is.Not.Null, $"Save {number} of {mapName} should have made a version; last result {clientSys.LastResult?.LocKey}.");
        return version!;
    }

    /// <summary>Waits for an export download to finish on the client and reads its one file from the data folder.</summary>
    private static async Task<byte[]> WaitExport(TestPair pair, string mapName, string version, string fileName)
    {
        var clientSys = pair.Client.EntMan.System<WFMappingClientSystem>();
        var clientRes = pair.Client.ResolveDependency<IResourceManager>();
        for (var i = 0; i < 600 && clientSys.Download is not { Active: false, Zip: true }; i++)
        {
            await pair.RunTicksSync(1);
        }

        var path = WFMappingLibraryNames.DownloadPath(mapName, version, fileName);
        byte[] data = default!;
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(clientSys.Download, Is.Not.Null, "The export should have finished.");
            Assert.That(clientSys.Download!.Zip, Is.True);
            Assert.That(clientSys.Download.FileCount, Is.EqualTo(1), "An export is one file.");
            Assert.That(clientRes.UserData.Exists(path), Is.True, $"{path} should be written.");
            data = Read(clientRes, path);
        });

        return data;
    }

    private static async Task<List<WFMappingExportEntry>> WaitZip(TestPair pair, string mapName, string version)
    {
        return WFMappingExportPackage.Unzip(await WaitExport(pair, mapName, version, WFMappingLibraryNames.DownloadZipFile(mapName)));
    }

    [Test]
    public async Task ShipExportsOneZipWithDetails()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var library = entMan.System<WFMappingLibrarySystem>();
        var export = entMan.System<WFMappingExportSystem>();
        var mapSys = entMan.System<MapSystem>();
        var meta = entMan.System<MetaDataSystem>();
        var transform = entMan.System<SharedTransformSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var serverRes = server.ResolveDependency<IResourceManager>();
        var clientSys = client.EntMan.System<WFMappingClientSystem>();
        await Enable(pair);
        var player = pair.Player!;

        var results = new List<string>();
        await client.WaitPost(() =>
        {
            clientSys.DownloadSaveTo = false;
            clientSys.ResultReceived += result => results.Add(result.LocKey);
        });

        WFMappingSession session = default!;
        Entity<MapGridComponent> grid = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(player, ShipName, out var created), Is.True);
            session = created!;
            grid = MakeGrid(entMan, mapSys, tileDefs, session.RootMap, 10, 3);
            meta.SetEntityName(grid, ShipName);
            var x = 0;
            foreach (var part in Parts)
            {
                entMan.SpawnEntity(part, new EntityCoordinates(grid, x++ + 0.5f, 1.5f));
            }
        });
        await pair.RunTicksSync(10);

        var version = await SaveAndWait(pair, ShipName, 1);
        Assert.Multiple(() =>
        {
            Assert.That(version.ShipDepth, Is.EqualTo(0), "One grid on one level saves as a ship.");
            Assert.That(version.Scans.ContainsKey(0), Is.True, "The save keeps the vessel scan beside the grid file.");
            Assert.That(version.ShipDetails, Is.Null, "No details yet.");
        });

        // A price under the appraisal is an error in the check, so the ship export is refused; the map export is not.
        var map = default(WFMappingLibrarySystem.LibraryMap);
        await server.WaitAssertion(() =>
        {
            Assert.That(library.TryFindVersion(player.UserId, ShipName, null, out map, out _), Is.True);
            var refused = export.TryBuild(player, map!, version, WFMappingExportKind.Ship, new WFMappingShipDetails { Price = 1 });
            Assert.That(refused, Is.Null, "A ship with a price under the appraisal floor isn't exported.");
            var asMap = export.TryBuild(player, map!, version, WFMappingExportKind.Map, null);
            Assert.That(asMap, Is.Not.Null, "The same version exports as a map.");
            var entries = WFMappingExportPackage.Unzip(asMap!);
            Assert.That(entries.Select(e => e.Path), Is.EquivalentTo(new[]
            {
                "Resources/Maps/_WF/Community/export_ship.yml",
                "Resources/Prototypes/_WF/Community/export_ship.yml",
                "README.txt",
            }));
            Assert.That(Text(entries, "Resources/Prototypes/_WF/Community/export_ship.yml"), Does.Contain("- type: gameMap"));
        });
        for (var i = 0; i < 50 && !results.Contains("wf-mapping-export-errors"); i++)
        {
            await pair.RunTicksSync(1);
        }

        Assert.That(results, Does.Contain("wf-mapping-export-errors"));

        // The real request: the ship with the mapper's details arrives on the client as one zip.
        var details = new WFMappingShipDetails
        {
            Description = "A fine ship.",
            Classes = new List<VesselClass> { VesselClass.Cargo },
            Group = ShipyardConsoleUiKey.Security,
            AutoEngines = false,
            Engines = new List<VesselEngine> { VesselEngine.Plasma },
            Access = "Command",
            RequireCrew = true,
        };
        results.Clear();
        await server.WaitPost(() => RaiseAs(entMan, player, new WFMappingExportRequest(ShipName, null, WFMappingExportKind.Ship, details)));
        var zip = await WaitZip(pair, ShipName, version.Name.Folder);

        byte[] serverGrid = default!;
        await server.WaitPost(() => serverGrid = Read(serverRes, version.Grids[0]));
        var proto = Text(zip, "Resources/Prototypes/_WF/Community/export_ship.yml");
        var readme = Text(zip, "README.txt");
        Assert.Multiple(() =>
        {
            Assert.That(results.Where(r => r.Contains("fail") || r.Contains("bad") || r.Contains("error")), Is.Empty);
            Assert.That(zip.Select(e => e.Path), Is.EquivalentTo(new[]
            {
                "Resources/SharedMaps/_WF/Community/export_ship.yml",
                "Resources/Prototypes/_WF/Community/export_ship.yml",
                "README.txt",
            }));
            Assert.That(zip.First(e => e.Path == "Resources/SharedMaps/_WF/Community/export_ship.yml").Data, Is.EqualTo(serverGrid),
                "The grid in the zip is the server's grid file byte for byte.");
            Assert.That(proto, Does.Contain("  id: WFExportShip\n"));
            Assert.That(proto, Does.Contain("  description: 'A fine ship.'\n"));
            Assert.That(proto, Does.Contain("  group: Security\n"));
            Assert.That(proto, Does.Contain("  access: Command\n"));
            Assert.That(proto, Does.Contain("  requireCrew: true\n"));
            Assert.That(proto, Does.Contain("  class:\n  - Cargo\n"));
            Assert.That(proto, Does.Contain("  engine:\n  - Plasma\n"));
            Assert.That(proto, Does.Contain("shuttlePath: /SharedMaps/_WF/Community/export_ship.yml"));
            Assert.That(proto.Split('\n')[0], Does.StartWith("# "), "The file opens with the header comment.");
            Assert.That(proto, Does.Contain("price, category"), "The header names the fields still guessed.");
            Assert.That(readme, Does.Contain("Resources/SharedMaps/_WF/Community/export_ship.yml"));
            Assert.That(readme, Does.Contain("Resources/Prototypes/_WF/Community/export_ship.yml"));
            Assert.That(readme, Does.Contain("Issues report"));
            Assert.That(readme, Does.Contain("No issues."), readme);
        });

        // The details persisted in the version, rewrote its vessel file, reach the listing and ride into the next save.
        await server.WaitAssertion(() =>
        {
            Assert.That(library.TryFindVersion(player.UserId, ShipName, null, out map, out var fresh), Is.True);
            Assert.That(fresh!.ShipDetails, Is.Not.Null, "ship.yml sits in the version folder.");
            var stored = library.ReadShipDetails(map!);
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Description, Is.EqualTo("A fine ship."));
            Assert.That(stored.Engines, Is.EqualTo(new[] { VesselEngine.Plasma }));
            Assert.That(serverRes.UserData.ReadAllText(fresh.Vessels[0]), Does.Contain("A fine ship."),
                "The version's own vessel file carries the details.");

            Assert.That(library.Autosave(session), Is.True);
            Assert.That(library.TryFindVersion(player.UserId, ShipName, null, out _, out var next), Is.True);
            Assert.That(next!.Name.Number, Is.EqualTo(2));
            Assert.That(next.ShipDetails, Is.Not.Null, "The next version carries the details on.");
            Assert.That(serverRes.UserData.ReadAllText(next.Vessels[0]), Does.Contain("A fine ship."));

            // An older version isn't what the session holds now, so it isn't checked against it: the price under the
            // floor goes through and the README says no check ran.
            Assert.That(library.TryFindVersion(player.UserId, ShipName, null, out map, out _), Is.True);
            var old = export.TryBuild(player, map!, version, WFMappingExportKind.Ship, new WFMappingShipDetails { Price = 1 });
            Assert.That(old, Is.Not.Null, "An older version exports unchecked.");
            Assert.That(Text(WFMappingExportPackage.Unzip(old!), "README.txt"), Does.Contain("No check ran"));
        });

        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            var listed = clientSys.Library?.Maps.FirstOrDefault(m => m.Name == ShipName);
            Assert.That(listed, Is.Not.Null);
            Assert.That(listed!.Ship, Is.Not.Null, "The listing carries the ship details.");
            Assert.That(listed.Ship!.Description, Is.EqualTo("A fine ship."));
            Assert.That(clientSys.ShipDetailsOf(ShipName)?.Access, Is.EqualTo("Command"));
            Assert.That(clientSys.IsShipVersion(ShipName, null), Is.True);
        });

        // Go to puts the observer at an issue's coordinates on the session's map.
        await server.WaitAssertion(() =>
        {
            var observer = player.AttachedEntity!.Value;
            var target = new EntityCoordinates(grid, 2.5f, 1.5f);
            RaiseAs(entMan, player, new WFMappingGoToIssueRequest(null, entMan.GetNetCoordinates(target)));
            Assert.That(transform.GetWorldPosition(observer),
                Is.EqualTo(transform.ToMapCoordinates(target).Position).Using<Vector2>((a, b) => Vector2.Distance(a, b) < 0.01f));
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MapExportsLevelsAndAZMap()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var library = entMan.System<WFMappingLibrarySystem>();
        var export = entMan.System<WFMappingExportSystem>();
        var zLevels = entMan.System<MappingZLevelsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var serverRes = server.ResolveDependency<IResourceManager>();
        var clientSys = client.EntMan.System<WFMappingClientSystem>();
        await Enable(pair);
        var player = pair.Player!;
        await client.WaitPost(() => clientSys.DownloadSaveTo = false);

        WFMappingSession session = default!;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(player, LevelsName, out var created), Is.True);
            session = created!;
            MakeGrid(entMan, mapSys, tileDefs, session.RootMap, 3, 2);
        });
        await pair.RunTicksSync(10);

        // One level: a gameMap at the single-level path.
        var single = await SaveAndWait(pair, LevelsName, 1);
        await server.WaitAssertion(() =>
        {
            Assert.That(library.TryFindVersion(player.UserId, LevelsName, null, out var map, out _), Is.True);
            var zip = export.TryBuild(player, map!, single, WFMappingExportKind.Map, null);
            Assert.That(zip, Is.Not.Null);
            var entries = WFMappingExportPackage.Unzip(zip!);
            Assert.That(entries.Select(e => e.Path), Is.EquivalentTo(new[]
            {
                "Resources/Maps/_WF/Community/export_levels.yml",
                "Resources/Prototypes/_WF/Community/export_levels.yml",
                "README.txt",
            }));
            var proto = Text(entries, "Resources/Prototypes/_WF/Community/export_levels.yml");
            Assert.That(proto, Does.Contain("- type: gameMap\n  id: WFExportLevels\n"));
            Assert.That(proto, Does.Contain("mapPath: /Maps/_WF/Community/export_levels.yml"));
            Assert.That(entries.First(e => e.Path == "Resources/Maps/_WF/Community/export_levels.yml").Data,
                Is.EqualTo(Read(serverRes, single.Levels[0])));
        });

        // Two levels: a level file each under the map's folder and a zMap listing them bottom-up.
        await server.WaitAssertion(() =>
        {
            Assert.That(zLevels.TryAddLevel(session.RootMap, true, out var upper, out var depth, out _), Is.True);
            Assert.That(depth, Is.EqualTo(1));
            MakeGrid(entMan, mapSys, tileDefs, upper, 2, 2);
        });
        await pair.RunTicksSync(5);

        // The manual save is throttled to one per ten seconds, so the second version is an autosave.
        WFMappingLibrarySystem.LibraryVersion stacked = default!;
        await server.WaitAssertion(() =>
        {
            Assert.That(library.Autosave(session), Is.True);
            Assert.That(library.TryFindVersion(player.UserId, LevelsName, null, out _, out var found), Is.True);
            stacked = found!;
        });
        Assert.That(stacked.Name.Number, Is.EqualTo(2));
        Assert.That(stacked.Levels.Keys, Is.EqualTo(new[] { 0, 1 }));

        await server.WaitPost(() => RaiseAs(entMan, player, new WFMappingExportRequest(LevelsName, null, WFMappingExportKind.Map, null)));
        var zipEntries = await WaitZip(pair, LevelsName, stacked.Name.Folder);
        byte[] ground = default!;
        byte[] upperLevel = default!;
        await server.WaitPost(() =>
        {
            ground = Read(serverRes, stacked.Levels[0]);
            upperLevel = Read(serverRes, stacked.Levels[1]);
        });

        var zMap = Text(zipEntries, "Resources/Prototypes/_WF/Community/export_levels.yml");
        var readme = Text(zipEntries, "README.txt");
        Assert.Multiple(() =>
        {
            Assert.That(zipEntries.Select(e => e.Path), Is.EquivalentTo(new[]
            {
                "Resources/Maps/_WF/Community/export_levels/z0.yml",
                "Resources/Maps/_WF/Community/export_levels/z1.yml",
                "Resources/Prototypes/_WF/Community/export_levels.yml",
                "README.txt",
            }));
            Assert.That(zipEntries.First(e => e.Path == "Resources/Maps/_WF/Community/export_levels/z0.yml").Data, Is.EqualTo(ground));
            Assert.That(zipEntries.First(e => e.Path == "Resources/Maps/_WF/Community/export_levels/z1.yml").Data, Is.EqualTo(upperLevel));
            Assert.That(zMap, Does.Contain("- type: zMap\n  id: WFExportLevels\n"));
            Assert.That(zMap, Does.Contain("  - /Maps/_WF/Community/export_levels/z0.yml\n  - /Maps/_WF/Community/export_levels/z1.yml\n"));
            Assert.That(zMap, Does.Contain("index 0"));
            Assert.That(readme, Does.Contain("Resources/Maps/_WF/Community/export_levels/z1.yml"));
            Assert.That(readme, Does.Contain("Resources/Prototypes/_WF/Community/export_levels.yml"));
            Assert.That(readme, Does.Contain("Issues report"));
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GridExportsTheBareGridFile()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var library = entMan.System<WFMappingLibrarySystem>();
        var export = entMan.System<WFMappingExportSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var serverRes = server.ResolveDependency<IResourceManager>();
        var clientSys = client.EntMan.System<WFMappingClientSystem>();
        await Enable(pair);
        var player = pair.Player!;

        var results = new List<string>();
        await client.WaitPost(() =>
        {
            clientSys.DownloadSaveTo = false;
            clientSys.ResultReceived += result => results.Add(result.LocKey);
        });

        WFMappingSession session = default!;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(player, GridName, out var created), Is.True);
            session = created!;
            MakeGrid(entMan, mapSys, tileDefs, session.RootMap, 4, 3);
        });
        await pair.RunTicksSync(10);

        // One grid on one level: the export is that grid file, under the ship grid's name, not a zip.
        var version = await SaveAndWait(pair, GridName, 1);
        Assert.That(version.ShipDepth, Is.EqualTo(0));
        // Through the client, as the wizard does: a grid file isn't told from a plain download by its name.
        await client.WaitPost(() => clientSys.RequestExport(GridName, null, WFMappingExportKind.Grid, null));
        var file = await WaitExport(pair, GridName, version.Name.Folder, "export_grid.yml");
        byte[] stored = default!;
        await server.WaitPost(() => stored = Read(serverRes, version.Grids[0]));
        var zipPath = WFMappingLibraryNames.DownloadPath(GridName, version.Name.Folder, WFMappingLibraryNames.DownloadZipFile(GridName));
        await client.WaitAssertion(() =>
        {
            Assert.That(results.Where(r => r.Contains("fail") || r.Contains("bad") || r.Contains("error")), Is.Empty);
            Assert.That(file, Is.EqualTo(stored), "The download is the stored grid file byte for byte.");
            Assert.That(client.ResolveDependency<IResourceManager>().UserData.Exists(zipPath), Is.False, "No zip was written.");
        });

        // Two grids on the level: refused the way a ship is.
        await server.WaitPost(() =>
        {
            var second = MakeGrid(entMan, mapSys, tileDefs, session.RootMap, 2, 2);
            entMan.System<SharedTransformSystem>().SetLocalPosition(second, new Vector2(12, 0));
        });
        await pair.RunTicksSync(5);
        results.Clear();
        await server.WaitAssertion(() =>
        {
            Assert.That(library.Autosave(session), Is.True);
            Assert.That(library.TryFindVersion(player.UserId, GridName, null, out var map, out var two), Is.True);
            Assert.That(two!.Name.Number, Is.EqualTo(2));
            Assert.That(two.TotalGrids, Is.EqualTo(2));
            Assert.That(export.TryBuild(player, map!, two, WFMappingExportKind.Grid, null), Is.Null, "Two grids aren't one grid file.");
            Assert.That(export.TryBuild(player, map!, two, WFMappingExportKind.Map, null), Is.Not.Null, "The same version exports as a map.");
        });
        for (var i = 0; i < 50 && !results.Contains("wf-mapping-ship-several-grids"); i++)
        {
            await pair.RunTicksSync(1);
        }

        Assert.That(results, Does.Contain("wf-mapping-ship-several-grids"));

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
