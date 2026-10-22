#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Server._WF.MappingMode;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.MappingMode;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// A level with one grid saves a grid file and a vessel prototype beside it: the grid file loads as a grid with the
/// same tiles and anchored entities, uploads back into the library, and downloads with the vessel as a ship; the vessel
/// text loads as a prototype. Versions with no grid or several are refused as ships. Opening a map puts the mapper
/// on its largest grid, and Go to moves them between grids.
/// </summary>
[TestFixture]
public sealed class WFMappingShipTest
{
    private const string MapName = "Ship Test";
    private const string Wall = "WallSolid";
    private const string Ame = "AmeController";

    private static List<string> Versions(IResourceManager res, NetUserId user, string map)
    {
        var folder = WFMappingLibraryNames.MapFolder(user, map);
        return res.UserData.IsDir(folder) ? res.UserData.DirectoryEntries(folder).Order().ToList() : new List<string>();
    }

    private static int Tiles(IEntityManager entMan, SharedMapSystem maps, EntityUid grid)
    {
        var count = 0;
        var tiles = maps.GetAllTiles(grid, entMan.GetComponent<MapGridComponent>(grid));
        while (tiles.MoveNext(out _))
        {
            count++;
        }

        return count;
    }

    private static int Anchored(IEntityManager entMan, EntityUid grid)
    {
        var count = 0;
        var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (entMan.GetComponent<TransformComponent>(child).Anchored)
                count++;
        }

        return count;
    }

    private static byte[] Read(IResourceManager res, ResPath path)
    {
        using var stream = res.UserData.OpenRead(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    [Test]
    public async Task ShipGridRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var library = entMan.System<WFMappingLibrarySystem>();
        var upload = entMan.System<WFMappingUploadSystem>();
        var mapSys = entMan.System<MapSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var transform = entMan.System<SharedTransformSystem>();
        var res = server.ResolveDependency<IResourceManager>();
        var clientRes = client.ResolveDependency<IResourceManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        WFMappingSession session = default!;
        Entity<MapGridComponent> grid = default;
        string shipVersion = string.Empty;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(WFMappingModeCVars.UploadCooldown, 0f);
            server.CfgMan.SetCVar(WFMappingModeCVars.ReferenceCooldown, 0f);

            // One 5x3 grid of plating with three walls and an AME controller.
            Assert.That(mapping.TryCreateSession(a, MapName, out var created), Is.True);
            session = created!;
            grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            var plating = new Tile(tileDefs["Plating"].TileId);
            var cells = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    cells.Add((new Vector2i(x, y), plating));
                }
            }

            mapSys.SetTiles(grid, grid.Comp, cells);
            for (var x = 0; x < 3; x++)
            {
                entMan.SpawnEntity(Wall, new EntityCoordinates(grid, x + 0.5f, 0.5f));
            }

            entMan.SpawnEntity(Ame, new EntityCoordinates(grid, 4.5f, 2.5f));

            RaiseAs(entMan, a, new WFMappingSaveRequest(string.Empty));
            var versions = Versions(res, a.UserId, MapName);
            Assert.That(versions, Has.Count.EqualTo(1));
            shipVersion = versions[0];
            Assert.Multiple(() =>
            {
                Assert.That(res.UserData.Exists(WFMappingLibraryNames.LevelPath(a.UserId, MapName, shipVersion, 0)), Is.True);
                Assert.That(res.UserData.Exists(WFMappingLibraryNames.GridPath(a.UserId, MapName, shipVersion, 0)), Is.True,
                    "A level with one grid keeps a grid file.");
                Assert.That(res.UserData.Exists(WFMappingLibraryNames.VesselPath(a.UserId, MapName, shipVersion, 0)), Is.True,
                    "The grid file comes with a vessel prototype.");
                Assert.That(library.TryFindVersion(a.UserId, MapName, null, out _, out var found), Is.True);
                Assert.That(found!.ShipDepth, Is.EqualTo(0), "The version is listed as a ship.");
                Assert.That(found.TotalGrids, Is.EqualTo(1));
                Assert.That(WFMappingDownloadSystem.TryGetFiles(MapName, found, false, out var single, out _, out _),
                    Is.True);
                Assert.That(single.Single().Name, Is.EqualTo("Ship Test.yml"));
                Assert.That(single.Single().Path, Is.EqualTo(found.Grids[0]),
                    "A single level with one grid downloads as its grid file.");
            });
        });

        await server.WaitAssertion(() =>
        {
            // The grid file loads with the shipyard's call onto a fresh map, with the same tiles and anchored entities.
            var gridPath = WFMappingLibraryNames.GridPath(a.UserId, MapName, shipVersion, 0);
            mapSys.CreateMap(out var freshId);
            Assert.That(loader.TryLoadGrid(freshId, gridPath, out var loaded), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(Tiles(entMan, mapSys, loaded!.Value), Is.EqualTo(Tiles(entMan, mapSys, grid)));
                Assert.That(Anchored(entMan, loaded.Value), Is.EqualTo(Anchored(entMan, grid)));
                Assert.That(Anchored(entMan, grid), Is.EqualTo(4));
            });
            mapSys.DeleteMap(freshId);

            // The vessel text loads as a prototype that points at the grid's place in the game.
            var vesselText = res.UserData.ReadAllText(WFMappingLibraryNames.VesselPath(a.UserId, MapName, shipVersion, 0));
            var id = WFMappingVesselYaml.VesselId(MapName);
            Assert.That(prototypes.HasIndex<VesselPrototype>(id), Is.False);

            // Reload only the vessel. ResolveResults would rebuild every prototype without a reload event, leaving the
            // tile definitions with tile id 0, so biomes on this pooled server would generate nothing from then on.
            var changed = new Dictionary<Type, HashSet<string>>();
            try
            {
                prototypes.LoadString(vesselText, changed: changed);
                prototypes.ReloadPrototypes(changed);
                Assert.That(prototypes.TryIndex<VesselPrototype>(id, out var vessel), Is.True, vesselText);
                Assert.Multiple(() =>
                {
                    Assert.That(vessel!.ID, Is.EqualTo("WFShipTest"));
                    Assert.That(vessel.Name, Is.EqualTo(MapName));
                    Assert.That(vessel.ShuttlePath, Is.EqualTo(new ResPath("/SharedMaps/_WF/Community/ship_test.yml")));
                    Assert.That(vessel.Price, Is.Positive);
                    Assert.That(vessel.Category, Is.EqualTo(VesselSize.Micro), "15 tiles is a micro ship.");
                    Assert.That(vessel.Engines, Does.Contain(VesselEngine.AME));
                    Assert.That(vessel.Classes, Is.EqualTo(new[] { VesselClass.Civilian }));
                });
            }
            finally
            {
                prototypes.RemoveString(vesselText);
                prototypes.ReloadPrototypes(new Dictionary<Type, HashSet<string>>(),
                    new Dictionary<Type, HashSet<string>> { { typeof(VesselPrototype), new HashSet<string> { id } } });
            }

            Assert.That(prototypes.HasIndex<VesselPrototype>(id), Is.False, "The throwaway vessel is gone again.");
            Assert.That(prototypes.Index<ContentTileDefinition>("FloorSteel").TileId,
                Is.EqualTo(server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId),
                "Loading the vessel rebuilt the tile definitions without their tile ids.");
        });

        // The grid file uploads back into the library, and is a ship there too.
        await server.WaitAssertion(() =>
        {
            var data = Read(res, WFMappingLibraryNames.GridPath(a.UserId, MapName, shipVersion, 0));
            const string uploadName = "Ship Upload";
            var chunks = WFMappingUploadAssembler.ChunksFor(data.Length);
            RaiseAs(entMan, a, new WFMappingUploadBeginRequest(uploadName, data.Length, chunks));
            Assert.That(upload.ActiveTransfer(a.UserId), Is.Not.Null, upload.LastOutcome(a.UserId));
            var transfer = upload.ActiveTransfer(a.UserId)!.Value;
            for (var i = 0; i < chunks; i++)
            {
                var offset = i * WFMappingUploadAssembler.ChunkSize;
                var length = Math.Min(WFMappingUploadAssembler.ChunkSize, data.Length - offset);
                RaiseAs(entMan, a, new WFMappingUploadChunkEvent(transfer, i, data.AsSpan(offset, length).ToArray()));
            }

            Assert.That(upload.LastOutcome(a.UserId), Is.EqualTo("wf-mapping-mode-upload-done"));
            Assert.That(library.TryFindVersion(a.UserId, uploadName, null, out _, out var uploaded), Is.True);
            Assert.That(uploaded!.Name.Kind, Is.EqualTo(WFMappingVersionKind.Upload));
            Assert.That(uploaded.ShipDepth, Is.EqualTo(0), "An uploaded grid file is a ship.");
        });

        // A ship download sends the grid file as <MapName>.yml and the vessel as <MapName>.vessel.yml.
        var player = pair.Player!;
        await server.WaitPost(() =>
        {
            foreach (var file in res.UserData.DirectoryEntries(WFMappingLibraryNames.VersionFolder(a.UserId, MapName, shipVersion)).ToList())
            {
                var target = WFMappingLibraryNames.VersionFolder(player.UserId, MapName, shipVersion) / file;
                res.UserData.CreateDir(target.Directory);
                res.UserData.WriteAllBytes(target,
                    Read(res, WFMappingLibraryNames.VersionFolder(a.UserId, MapName, shipVersion) / file));
            }

            RaiseAs(entMan, player, new WFMappingDownloadRequest(MapName, null, ship: true));
        });

        var shipPath = WFMappingLibraryNames.DownloadPath(MapName, shipVersion, "Ship Test.yml");
        var vesselPath = WFMappingLibraryNames.DownloadPath(MapName, shipVersion, "Ship Test.vessel.yml");
        for (var i = 0; i < 300 && !clientRes.UserData.Exists(vesselPath); i++)
        {
            await pair.RunTicksSync(1);
        }

        var serverGrid = Read(res, WFMappingLibraryNames.GridPath(a.UserId, MapName, shipVersion, 0));
        var serverVessel = Read(res, WFMappingLibraryNames.VesselPath(a.UserId, MapName, shipVersion, 0));
        await client.WaitAssertion(() =>
        {
            Assert.That(clientRes.UserData.Exists(shipPath), Is.True, "The ship grid arrives as <MapName>.yml.");
            Assert.That(clientRes.UserData.Exists(vesselPath), Is.True, "The vessel arrives as <MapName>.vessel.yml.");
            Assert.That(Read(clientRes, shipPath), Is.EqualTo(serverGrid));
            Assert.That(Read(clientRes, vesselPath), Is.EqualTo(serverVessel));
        });

        // A second grid: the next version keeps no grid file, downloads as its map file and is refused as a ship.
        Entity<MapGridComponent> big = default;
        await server.WaitAssertion(() =>
        {
            big = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            transform.SetLocalPosition(big, new Vector2(40, 40));
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(big, big.Comp, Enumerable.Range(0, 30).Select(x => (new Vector2i(x, 0), plating)).ToList());

            Assert.That(library.Autosave(session), Is.True);
            Assert.That(library.TryFindVersion(a.UserId, MapName, null, out _, out var two), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(two!.TotalGrids, Is.EqualTo(2));
                Assert.That(two.Grids, Is.Empty, "A level with two grids keeps no grid file.");
                Assert.That(two.ShipDepth, Is.Null);
                Assert.That(WFMappingDownloadSystem.TryGetFiles(MapName, two, true, out _, out var error, out var args),
                    Is.False);
                Assert.That(error, Is.EqualTo("wf-mapping-ship-several-grids"));
                Assert.That(args.Single().Item2, Is.EqualTo(2));
                Assert.That(WFMappingDownloadSystem.TryGetFiles(MapName, two, false, out var files, out _, out _), Is.True);
                Assert.That(files.Single().Name, Is.EqualTo("Ship Test.yml"));
                Assert.That(files.Single().Path, Is.EqualTo(two.Levels[0]), "With no grid file the map file goes.");
            });

            // A version with no grid at all is refused too.
            Assert.That(mapping.TryCreateSession(a, "Empty Ship", out var empty), Is.True);
            Assert.That(library.Autosave(empty!), Is.True);
            Assert.That(library.TryFindVersion(a.UserId, "Empty Ship", null, out _, out var none), Is.True);
            Assert.That(WFMappingDownloadSystem.TryGetFiles("Empty Ship", none!, true, out _, out var noneError, out _),
                Is.False);
            Assert.That(noneError, Is.EqualTo("wf-mapping-ship-no-grid"));
            mapping.LeaveSession(a.UserId, kicked: false);
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            // Opening the two-grid version puts the mapper at the centre of the bigger grid.
            RaiseAs(entMan, a, new WFMappingOpenRequest(MapName, null));
            Assert.That(mapping.TryGetPresent(a, out var opened), Is.True);
            var observer = a.AttachedEntity!.Value;
            var grids = mapping.GridsOn(opened!.RootMap);
            Assert.That(grids, Has.Count.EqualTo(2));
            Assert.That(grids[0].Tiles, Is.EqualTo(30), "The list is largest first.");
            Assert.That(grids[0].Entities, Is.EqualTo(0));
            Assert.That(grids[1].Entities, Is.EqualTo(4));

            var bigUid = entMan.GetEntity(grids[0].Grid);
            var smallUid = entMan.GetEntity(grids[1].Grid);
            Assert.That(transform.GetWorldPosition(observer),
                Is.EqualTo(mapping.GridCentre(bigUid)).Using<Vector2>((x, y) => Vector2.Distance(x, y) < 0.01f),
                "The mapper starts on the largest grid.");

            // Go to moves them to the other grid's centre.
            RaiseAs(entMan, a, new WFMappingGoToGridRequest(grids[1].Grid));
            Assert.That(transform.GetWorldPosition(observer),
                Is.EqualTo(mapping.GridCentre(smallUid)).Using<Vector2>((x, y) => Vector2.Distance(x, y) < 0.01f));
            Assert.That(transform.GetMapId(observer), Is.EqualTo(entMan.GetComponent<MapComponent>(opened.RootMap).MapId));
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
