#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Client._WF.MappingMode;
using Content.IntegrationTests.Pair;
using Content.Server._WF.MappingMode;
using Content.Shared._WF.MappingMode;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// A session the real client saved downloads to the client byte for byte, as a map and as a ship; Save to with no
/// dialog writes nothing to the data folder; and an empty library file is refused instead of arriving empty.
/// </summary>
[TestFixture]
public sealed class WFMappingDownloadTest
{
    private const string MapName = "Download Ship";
    private const string EmptyMap = "Empty Map";

    [Test]
    public async Task SavedSessionDownloadsWhole()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var library = entMan.System<WFMappingLibrarySystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var serverRes = server.ResolveDependency<IResourceManager>();
        var clientRes = client.ResolveDependency<IResourceManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var clientSys = client.EntMan.System<WFMappingClientSystem>();
        await Enable(pair);
        var player = pair.Player!;

        var results = new List<string>();
        await client.WaitPost(() =>
        {
            clientSys.DownloadSaveTo = false;
            clientSys.ResultReceived += result => results.Add(result.LocKey);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(player, MapName, out var session), Is.True);
            var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session!.RootMap).MapId);
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp,
                Enumerable.Range(0, 6).Select(x => (new Vector2i(x % 3, x / 3), plating)).ToList());
        });
        await pair.RunTicksSync(10);

        // The client saves through the network, as the Save tab does.
        await client.WaitPost(() => clientSys.Send(new WFMappingSaveRequest(string.Empty)));
        WFMappingLibrarySystem.LibraryVersion? version = null;
        for (var i = 0; i < 100 && version == null; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() =>
            {
                if (library.TryFindVersion(player.UserId, MapName, null, out _, out var found) && found.ShipDepth == 0)
                    version = found;
            });
        }

        Assert.That(version, Is.Not.Null, "The save should have made a ship version.");
        var folder = version!.Name.Folder;
        byte[] grid = default!;
        byte[] vessel = default!;
        await server.WaitPost(() =>
        {
            grid = Read(serverRes, version.Grids[0]);
            vessel = Read(serverRes, version.Vessels[0]);
        });
        Assert.That(grid, Is.Not.Empty, "The server's grid file is empty.");
        Assert.That(vessel, Is.Not.Empty, "The server's vessel file is empty.");

        // A plain download of a one-level version with one grid sends the grid file as <map>.yml.
        var mapPath = WFMappingLibraryNames.DownloadPath(MapName, folder, WFMappingLibraryNames.DownloadMapFile(MapName));
        var vesselPath = WFMappingLibraryNames.DownloadPath(MapName, folder, WFMappingLibraryNames.DownloadVesselFile(MapName));
        await client.WaitPost(() => clientSys.RequestDownload(MapName, null));
        await WaitDone(pair, clientSys, ship: false);
        await client.WaitAssertion(() =>
        {
            Assert.That(clientSys.Download, Is.Not.Null, "The download should have finished.");
            Assert.That(clientSys.Download!.FileCount, Is.EqualTo(1));
            Assert.That(clientRes.UserData.Exists(mapPath), Is.True);
            Assert.That(Read(clientRes, mapPath), Is.EqualTo(grid), "The map file should match the server's byte for byte.");
            Assert.That(clientRes.UserData.Exists(vesselPath), Is.False, "A plain download carries no vessel file.");
        });

        // A ship download sends the grid, then the vessel prototype.
        await WaitThrottle(pair, timing);
        await client.WaitPost(() =>
        {
            clientRes.UserData.Delete(mapPath);
            clientSys.RequestShipDownload(MapName, null);
        });
        await WaitDone(pair, clientSys, ship: true);
        await client.WaitAssertion(() =>
        {
            Assert.That(clientSys.Download!.FileCount, Is.EqualTo(2));
            Assert.That(Read(clientRes, mapPath), Is.EqualTo(grid), "The ship's grid should match the server's.");
            Assert.That(Read(clientRes, vesselPath), Is.EqualTo(vessel), "The vessel prototype should match the server's.");
            Assert.That(results, Does.Not.Contain("wf-mapping-mode-download-verify-failed"));
        });

        // Save to asks a dialog per file; a headless client has none, so both count as cancelled and nothing lands
        // in the data folder.
        await WaitThrottle(pair, timing);
        await client.WaitPost(() =>
        {
            clientRes.UserData.Delete(WFMappingLibraryNames.DownloadRoot / MapName);
            clientSys.DownloadSaveTo = true;
            results.Clear();
            clientSys.RequestShipDownload(MapName, null);
        });
        for (var i = 0; i < 300 && !results.Contains("wf-mapping-mode-download-save-skipped"); i++)
        {
            await pair.RunTicksSync(1);
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(results, Does.Contain("wf-mapping-mode-download-save-skipped"));
            Assert.That(clientSys.Download, Is.Not.Null);
            Assert.That(clientSys.Download!.SaveTo, Is.True);
            Assert.That(clientSys.Download.Asking, Is.Null);
            Assert.That(clientSys.Download.Saved, Is.Zero);
            Assert.That(clientSys.DownloadFolder, Is.Null, "Save to has no folder to open.");
            Assert.That(clientRes.UserData.Exists(mapPath), Is.False);
            Assert.That(clientSys.DownloadBusy, Is.False);
            clientSys.DownloadSaveTo = false;
        });

        // An empty library file is refused with a reason, not sent as an empty file.
        var emptyVersion = new WFMappingVersionName(1, new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
            WFMappingVersionKind.Manual).Folder;
        await server.WaitPost(() =>
        {
            var path = WFMappingLibraryNames.LevelPath(player.UserId, EmptyMap, emptyVersion, 0);
            serverRes.UserData.CreateDir(path.Directory);
            using var stream = serverRes.UserData.OpenWrite(path);
        });
        await WaitThrottle(pair, timing);
        await client.WaitPost(() =>
        {
            results.Clear();
            clientSys.RequestDownload(EmptyMap, null);
        });
        for (var i = 0; i < 100 && !results.Contains("wf-mapping-mode-download-empty"); i++)
        {
            await pair.RunTicksSync(1);
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(results, Does.Contain("wf-mapping-mode-download-empty"));
            Assert.That(clientRes.UserData.Exists(WFMappingLibraryNames.DownloadPath(EmptyMap, emptyVersion,
                WFMappingLibraryNames.DownloadMapFile(EmptyMap))), Is.False, "Nothing should be written for an empty file.");
            Assert.That(clientSys.DownloadBusy, Is.False);
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MultiLevelDownloadsEveryLevel()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var library = entMan.System<WFMappingLibrarySystem>();
        var zLevels = entMan.System<Content.Server._WF.MappingTools.MappingZLevelsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var clientRes = client.ResolveDependency<IResourceManager>();
        var clientSys = client.EntMan.System<WFMappingClientSystem>();
        await Enable(pair);
        var player = pair.Player!;
        const string name = "Download Levels";

        var results = new List<string>();
        await client.WaitPost(() =>
        {
            clientSys.DownloadSaveTo = false;
            clientSys.ResultReceived += result => results.Add(result.LocKey);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(player, name, out var session), Is.True);
            Assert.That(zLevels.TryAddLevel(session!.RootMap, true, out var upper, out _, out _), Is.True);
            var plating = new Tile(tileDefs["Plating"].TileId);
            foreach (var level in new[] { session.RootMap, upper })
            {
                var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(level).MapId);
                mapSys.SetTiles(grid, grid.Comp,
                    Enumerable.Range(0, 6).Select(x => (new Vector2i(x % 3, x / 3), plating)).ToList());
            }
        });
        await pair.RunTicksSync(10);

        await client.WaitPost(() => clientSys.Send(new WFMappingSaveRequest(string.Empty)));
        WFMappingLibrarySystem.LibraryVersion? version = null;
        for (var i = 0; i < 100 && version == null; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() =>
            {
                if (library.TryFindVersion(player.UserId, name, null, out _, out var found))
                    version = found;
            });
        }

        Assert.That(version, Is.Not.Null, "The save should have made a version.");
        Assert.That(version!.Levels, Has.Count.EqualTo(2));

        await client.WaitPost(() => clientSys.RequestDownload(name, null));
        await WaitDone(pair, clientSys, ship: false);
        await client.WaitAssertion(() =>
        {
            Assert.That(results.Where(r => r.Contains("fail") || r.Contains("empty") || r.Contains("bad") || r.Contains("large")),
                Is.Empty, "The download should raise no error.");
            Assert.That(clientSys.Download, Is.Not.Null, "The download should have finished.");
            Assert.That(clientSys.Download!.FileCount, Is.EqualTo(2));
            foreach (var depth in new[] { 0, 1 })
            {
                var path = WFMappingLibraryNames.DownloadPath(name, version.Name.Folder,
                    WFMappingLibraryNames.DownloadLevelFile(name, depth));
                Assert.That(clientRes.UserData.Exists(path), Is.True, $"Level {depth} should be downloaded.");
            }
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>Waits for a download of the given kind to finish on the client.</summary>
    private static async Task WaitDone(TestPair pair, WFMappingClientSystem clientSys, bool ship)
    {
        for (var i = 0; i < 300; i++)
        {
            if (clientSys.Download is { Active: false } done && done.Ship == ship)
                return;

            await pair.RunTicksSync(1);
        }
    }

    /// <summary>Runs the server past the one-per-second download request throttle.</summary>
    private static async Task WaitThrottle(TestPair pair, IGameTiming timing)
    {
        var start = timing.CurTime;
        for (var i = 0; i < 600 && timing.CurTime - start < TimeSpan.FromSeconds(1.2); i++)
        {
            await pair.RunTicksSync(1);
        }
    }

    private static byte[] Read(IResourceManager res, ResPath path)
    {
        using var stream = res.UserData.OpenRead(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
