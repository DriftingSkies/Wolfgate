#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingMode;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// Saves go into numbered versions, autosaves are marked and pruned first, opening a version restores its levels, and
/// a download arrives on the client byte for byte.
/// </summary>
[TestFixture]
public sealed class WFMappingLibraryTest
{
    private const string MapName = "Library Test";

    private static List<string> Versions(IResourceManager res, NetUserId user, string map)
    {
        var folder = WFMappingLibraryNames.MapFolder(user, map);
        return res.UserData.IsDir(folder) ? res.UserData.DirectoryEntries(folder).Order().ToList() : new List<string>();
    }

    [Test]
    public async Task SaveAutosavePruneAndOpen()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var library = entMan.System<WFMappingLibrarySystem>();
        var zLevels = entMan.System<MappingZLevelsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var res = server.ResolveDependency<IResourceManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        WFMappingSession session = default!;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, MapName, out var created), Is.True);
            session = created!;
            Assert.That(zLevels.TryAddLevel(session.RootMap, true, out var upper, out _, out _), Is.True);
            Assert.That(mapping.IsSessionMap(upper, session), Is.True, "A new level joins the session.");

            var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(0, 5).Select(x => (new Vector2i(x, 0), plating)).ToList());
            Assert.That(session.Dirty, Is.True, "Laying tiles marks the session changed.");
        });

        await server.WaitAssertion(() =>
        {
            // A manual save is version 1.
            RaiseAs(entMan, a, new WFMappingSaveRequest(string.Empty));
            var versions = Versions(res, a.UserId, MapName);
            Assert.That(versions, Has.Count.EqualTo(1));
            Assert.That(WFMappingLibraryNames.TryParseVersion(versions[0], out var first), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(first.Number, Is.EqualTo(1));
                Assert.That(first.Kind, Is.EqualTo(WFMappingVersionKind.Manual));
                Assert.That(res.UserData.Exists(WFMappingLibraryNames.LevelPath(a.UserId, MapName, versions[0], 0)), Is.True);
                Assert.That(res.UserData.Exists(WFMappingLibraryNames.LevelPath(a.UserId, MapName, versions[0], 1)), Is.True);
                Assert.That(session.Dirty, Is.False);
                Assert.That(session.LastSaved, Is.Not.Null);
            });

            // A second save so soon is refused; an autosave is version 2 and marked auto.
            RaiseAs(entMan, a, new WFMappingSaveRequest(string.Empty));
            Assert.That(Versions(res, a.UserId, MapName), Has.Count.EqualTo(1));
            Assert.That(library.Autosave(session), Is.True);
            versions = Versions(res, a.UserId, MapName);
            Assert.That(versions, Has.Count.EqualTo(2));
            Assert.That(WFMappingLibraryNames.TryParseVersion(versions[1], out var second), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(second.Number, Is.EqualTo(2));
                Assert.That(second.Kind, Is.EqualTo(WFMappingVersionKind.Auto));
            });

            // At max_versions the oldest autosave goes before any manual save.
            server.CfgMan.SetCVar(WFMappingModeCVars.MaxVersions, 2);
            Assert.That(library.Autosave(session), Is.True);
            versions = Versions(res, a.UserId, MapName);
            var numbers = versions.Select(v => WFMappingLibraryNames.TryParseVersion(v, out var n) ? n.Number : -1).ToList();
            Assert.That(numbers, Is.EqualTo(new[] { 1, 3 }));
            server.CfgMan.SetCVar(WFMappingModeCVars.MaxVersions, WFMappingModeCVars.MaxVersions.DefaultValue);

            mapping.LeaveSession(a.UserId, kicked: false);
            Assert.That(a.AttachedEntity, Is.Null);
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            // Opening the newest version brings both levels back as a new session.
            RaiseAs(entMan, a, new WFMappingOpenRequest(MapName, null));
            Assert.That(mapping.TryGetPresent(a, out var opened), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(opened, Is.Not.SameAs(session));
                Assert.That(opened!.SourceMap, Is.EqualTo(MapName));
                Assert.That(WFMappingLibraryNames.TryParseVersion(opened.SourceVersion!, out var source) && source.Number == 3,
                    Is.True);
                Assert.That(mapping.SessionMaps(opened.Id), Has.Count.EqualTo(2));
                Assert.That(zLevels.GetLevels(opened.RootMap, out _), Has.Count.EqualTo(2));
                Assert.That(opened.Dirty, Is.False);
            });

            var tiles = 0;
            var children = entMan.GetComponent<TransformComponent>(opened!.RootMap).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (!entMan.TryGetComponent(child, out MapGridComponent? grid))
                    continue;

                var enumerator = mapSys.GetAllTiles(child, grid);
                while (enumerator.MoveNext(out _))
                {
                    tiles++;
                }
            }

            Assert.That(tiles, Is.EqualTo(5), "The ground level keeps its tiles.");
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DownloadReassembles()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var serverRes = server.ResolveDependency<IResourceManager>();
        var clientRes = client.ResolveDependency<IResourceManager>();
        await Enable(pair);

        const string map = "Download Test";
        var version = new WFMappingVersionName(1, new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            WFMappingVersionKind.Manual).Folder;

        // Level files don't have to be valid maps to download; three and a bit chunks for the ground, one for the next.
        var random = new Random(42);
        var ground = new byte[WFMappingDownloadAssembler.ChunkSize * 3 + 1234];
        random.NextBytes(ground);
        var upper = new byte[777];
        random.NextBytes(upper);

        var user = pair.Player!.UserId;
        await server.WaitPost(() =>
        {
            foreach (var (depth, data) in new[] { (0, ground), (1, upper) })
            {
                var path = WFMappingLibraryNames.LevelPath(user, map, version, depth);
                serverRes.UserData.CreateDir(path.Directory);
                using var stream = serverRes.UserData.OpenWrite(path);
                stream.Write(data);
            }

            RaiseAs(server.EntMan, pair.Player!, new WFMappingDownloadRequest(map, null));
        });

        // Several levels arrive as <MapName>_z<D>.yml.
        var groundPath = WFMappingLibraryNames.DownloadPath(map, version, "Download Test_z0.yml");
        var upperPath = WFMappingLibraryNames.DownloadPath(map, version, "Download Test_z1.yml");
        for (var i = 0; i < 300 && !clientRes.UserData.Exists(upperPath); i++)
        {
            await pair.RunTicksSync(1);
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(clientRes.UserData.Exists(groundPath), Is.True, "The ground level should have arrived.");
            Assert.That(clientRes.UserData.Exists(upperPath), Is.True, "The second level should have arrived.");
            Assert.That(Read(clientRes, groundPath), Is.EqualTo(ground));
            Assert.That(Read(clientRes, upperPath), Is.EqualTo(upper));

            var download = client.EntMan.System<Content.Client._WF.MappingMode.WFMappingClientSystem>().Download;
            Assert.That(download?.Active ?? false, Is.False, "The download is finished.");
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    private static byte[] Read(IResourceManager res, ResPath path)
    {
        using var stream = res.UserData.OpenRead(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
