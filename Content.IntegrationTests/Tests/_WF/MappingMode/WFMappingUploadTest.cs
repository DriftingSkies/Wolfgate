#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Content.Server._WF.MappingMode;
using Content.Shared._WF.MappingMode;
using Content.Shared.Singularity.Components;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// An uploaded map file is checked, stripped and stored as an upload version that opens as a session; files over the
/// size or grid cap or with anchors, and uploads while busy or cooling down are refused, and no scratch map is left
/// behind. A file with an unknown prototype waits for a confirm and is stored without those entities.
/// </summary>
[TestFixture]
public sealed class WFMappingUploadTest
{
    private const string MapName = "Upload Test";
    private const string Wall = "WallSolid";
    private const string Missing = "WFUploadTestMissingPrototype";
    private const string OnWall = "Crowbar";
    private const string Loose = "Wrench";

    private static List<string> Versions(IResourceManager res, NetUserId user, string map)
    {
        var folder = WFMappingLibraryNames.MapFolder(user, map);
        return res.UserData.IsDir(folder) ? res.UserData.DirectoryEntries(folder).Order().ToList() : new List<string>();
    }

    [Test]
    public async Task UploadStoreOpenAndRefuse()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var library = entMan.System<WFMappingLibrarySystem>();
        var upload = entMan.System<WFMappingUploadSystem>();
        var mapSys = entMan.System<MapSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var res = server.ResolveDependency<IResourceManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        // The uploader, then one mapper per refusal, since each player may only send a couple of requests a second.
        var dummies = await AddMappers(pair, 8);
        var a = dummies[0];

        bool AnyScratch() => entMan.EntityQuery<WFMappingSessionMapComponent>(true).Any(m => m.Kind == WFMappingMapKind.Scratch);
        int Maps() => entMan.EntityQuery<MapComponent>(true).Count();

        // Sends a whole file through the upload events; the outcome's Fluent id.
        string? Upload(ICommonSession player, string name, byte[] data)
        {
            var chunks = WFMappingUploadAssembler.ChunksFor(data.Length);
            RaiseAs(entMan, player, new WFMappingUploadBeginRequest(name, data.Length, chunks));
            if (upload.ActiveTransfer(player.UserId) is not { } transfer)
                return upload.LastOutcome(player.UserId);

            for (var i = 0; i < chunks; i++)
            {
                var offset = i * WFMappingUploadAssembler.ChunkSize;
                var length = Math.Min(WFMappingUploadAssembler.ChunkSize, data.Length - offset);
                RaiseAs(entMan, player, new WFMappingUploadChunkEvent(transfer, i, data.AsSpan(offset, length).ToArray()));
            }

            Assert.That(upload.ActiveTransfer(player.UserId), Is.Null, "A complete upload is no longer running.");
            return upload.LastOutcome(player.UserId);
        }

        var yaml = string.Empty;
        var missingYaml = string.Empty;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(WFMappingModeCVars.UploadCooldown, 0f);
            server.CfgMan.SetCVar(WFMappingModeCVars.ReferenceCooldown, 0f);

            // A session map with a strip of plating, a wall, and a wall carrying a denied component.
            Assert.That(mapping.TryCreateSession(a, "Upload Source", out var source), Is.True);
            var mapId = entMan.GetComponent<MapComponent>(source!.RootMap).MapId;
            var grid = mapSys.CreateGridEntity(mapId);
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(0, 5).Select(x => (new Vector2i(x, 0), plating)).ToList());
            var wall = entMan.SpawnEntity(Wall, new EntityCoordinates(grid, 1.5f, 0.5f));
            var denied = entMan.SpawnEntity(Wall, new EntityCoordinates(grid, 3.5f, 0.5f));
            entMan.AddComponent<SingularityDistortionComponent>(denied);

            using var writer = new StringWriter();
            Assert.That(loader.TrySaveMap(source.RootMap, writer), Is.True);
            yaml = writer.ToString();
            Assert.That(yaml, Does.Contain("SingularityDistortion"));
            Assert.That(yaml, Does.Contain($"proto: {Wall}"));

            // The same map with an item parented to a wall and one loose on the grid, and the walls' prototype unknown.
            entMan.SpawnEntity(OnWall, new EntityCoordinates(wall, 0f, 0f));
            entMan.SpawnEntity(Loose, new EntityCoordinates(grid, 2.5f, 0.5f));
            using var withItems = new StringWriter();
            Assert.That(loader.TrySaveMap(source.RootMap, withItems), Is.True);
            missingYaml = withItems.ToString().Replace($"proto: {Wall}", $"proto: {Missing}");
            Assert.That(missingYaml, Does.Contain($"proto: {OnWall}").And.Contain($"proto: {Missing}"));

            // Uploaded from inside the session: stored as the first upload version.
            Assert.That(Upload(a, MapName, Encoding.UTF8.GetBytes(yaml)), Is.EqualTo("wf-mapping-mode-upload-done"));
            var versions = Versions(res, a.UserId, MapName);
            Assert.That(versions, Has.Count.EqualTo(1));
            Assert.That(WFMappingLibraryNames.TryParseVersion(versions[0], out var version), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(version.Number, Is.EqualTo(1));
                Assert.That(version.Kind, Is.EqualTo(WFMappingVersionKind.Upload));
                Assert.That(versions[0], Does.StartWith("v0001_").And.EndWith("_upload"));
                Assert.That(library.TryFindVersion(a.UserId, MapName, null, out _, out var listed), Is.True);
                Assert.That(listed?.Name, Is.EqualTo(version));
                Assert.That(AnyScratch(), Is.False, "The scratch map goes once stored.");
            });

            var stored = res.UserData.ReadAllText(WFMappingLibraryNames.LevelPath(a.UserId, MapName, versions[0], 0));
            Assert.Multiple(() =>
            {
                Assert.That(stored, Does.Not.Contain("SingularityDistortion"), "The denied entity is stripped.");
                Assert.That(stored, Does.Contain($"proto: {Wall}"));
            });

            mapping.LeaveSession(a.UserId, kicked: false);
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            // Opening the upload makes a session with the tiles and the wall that wasn't denied.
            RaiseAs(entMan, a, new WFMappingOpenRequest(MapName, null));
            Assert.That(mapping.TryGetPresent(a, out var opened), Is.True);

            var tiles = 0;
            var walls = 0;
            var distortions = 0;
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

                var onGrid = entMan.GetComponent<TransformComponent>(child).ChildEnumerator;
                while (onGrid.MoveNext(out var entity))
                {
                    if (entMan.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID == Wall)
                        walls++;

                    if (entMan.HasComponent<SingularityDistortionComponent>(entity))
                        distortions++;
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(opened.SourceMap, Is.EqualTo(MapName));
                Assert.That(tiles, Is.EqualTo(5));
                Assert.That(walls, Is.EqualTo(1));
                Assert.That(distortions, Is.EqualTo(0));
            });
        });

        await server.WaitAssertion(() =>
        {
            var maps = Maps();
            var valid = Encoding.UTF8.GetBytes(yaml);

            void Refused(ICommonSession player, string name, byte[] data, string expected, string why)
            {
                var before = Versions(res, player.UserId, name).Count;
                Assert.Multiple(() =>
                {
                    Assert.That(Upload(player, name, data), Is.EqualTo(expected), why);
                    Assert.That(Versions(res, player.UserId, name), Has.Count.EqualTo(before), $"{why}: nothing is stored.");
                    Assert.That(upload.ActiveTransfer(player.UserId), Is.Null, $"{why}: no transfer is left running.");
                    Assert.That(AnyScratch(), Is.False, $"{why}: no scratch map is left.");
                    Assert.That(Maps(), Is.EqualTo(maps), $"{why}: no map is left.");
                });
            }

            server.CfgMan.SetCVar(WFMappingModeCVars.MaxUploadBytes, 100L);
            Refused(dummies[1], "Too Big", valid, "wf-mapping-mode-upload-too-large", "Over the size cap");
            server.CfgMan.SetCVar(WFMappingModeCVars.MaxUploadBytes, WFMappingModeCVars.MaxUploadBytes.DefaultValue);

            var anchors = "meta: &meta\n  format: 7\n  category: Map\nentities:\n- proto: \"\"\n  entities: []\nother: *meta\n";
            Refused(dummies[2], "Anchors", Encoding.UTF8.GetBytes(anchors), "wf-mapping-mode-upload-anchors", "Anchors");

            server.CfgMan.SetCVar(WFMappingModeCVars.MaxGridsPerSession, 0);
            Refused(dummies[4], "Grid Cap", valid, "wf-mapping-mode-limit-grids", "Over the grid cap");
            server.CfgMan.SetCVar(WFMappingModeCVars.MaxGridsPerSession, WFMappingModeCVars.MaxGridsPerSession.DefaultValue);

            // An upload already running.
            var b = dummies[5];
            RaiseAs(entMan, b, new WFMappingUploadBeginRequest("Busy", valid.Length, WFMappingUploadAssembler.ChunksFor(valid.Length)));
            var running = upload.ActiveTransfer(b.UserId);
            Assert.That(running, Is.Not.Null);
            RaiseAs(entMan, b, new WFMappingUploadBeginRequest("Busy", valid.Length, WFMappingUploadAssembler.ChunksFor(valid.Length)));
            Assert.Multiple(() =>
            {
                Assert.That(upload.LastOutcome(b.UserId), Is.EqualTo("wf-mapping-mode-upload-busy"));
                Assert.That(upload.ActiveTransfer(b.UserId), Is.EqualTo(running), "The first upload keeps running.");
            });

            RaiseAs(entMan, b, new WFMappingUploadCancelRequest(running!.Value));
            Assert.Multiple(() =>
            {
                Assert.That(upload.ActiveTransfer(b.UserId), Is.Null);
                Assert.That(upload.LastOutcome(b.UserId), Is.EqualTo("wf-mapping-mode-upload-cancelled"));
            });

            // Inside the cooldown of an upload that started.
            var c = dummies[6];
            server.CfgMan.SetCVar(WFMappingModeCVars.UploadCooldown, 30f);
            RaiseAs(entMan, c, new WFMappingUploadBeginRequest("Cooldown", valid.Length, WFMappingUploadAssembler.ChunksFor(valid.Length)));
            RaiseAs(entMan, c, new WFMappingUploadCancelRequest(upload.ActiveTransfer(c.UserId)!.Value));
            Refused(c, "Cooldown", valid, "wf-mapping-mode-upload-cooldown", "Inside the cooldown");
            server.CfgMan.SetCVar(WFMappingModeCVars.UploadCooldown, 0f);
        });

        // An unknown prototype: held unloaded until confirmed, then stored without its entities and their children.
        var d = dummies[3];
        var e = dummies[7];
        var missingBytes = Encoding.UTF8.GetBytes(missingYaml);
        var expired = 0;
        await server.WaitAssertion(() =>
        {
            var maps = Maps();
            Assert.That(Upload(d, "Missing", missingBytes), Is.EqualTo("wf-mapping-mode-upload-needs-confirm"));
            var question = upload.PendingConfirmation(d.UserId);
            Assert.That(question, Is.Not.Null, "The upload waits for a confirm.");
            Assert.Multiple(() =>
            {
                Assert.That(question!.MissingPrototypes, Is.EqualTo(new[] { Missing }));
                Assert.That(question.MissingPrototypeCount, Is.EqualTo(1));
                Assert.That(question.EntitiesToDrop, Is.EqualTo(3), "Both walls and the item on one of them.");
                Assert.That(Versions(res, d.UserId, "Missing"), Is.Empty);
                Assert.That(AnyScratch(), Is.False, "Nothing loads before the confirm.");
                Assert.That(Maps(), Is.EqualTo(maps));
            });

            RaiseAs(entMan, d, new WFMappingUploadConfirmRequest(question!.TransferId, false));
            Assert.Multiple(() =>
            {
                Assert.That(upload.PendingConfirmation(d.UserId), Is.Not.Null, "A confirm without DropMissing does nothing.");
                Assert.That(Versions(res, d.UserId, "Missing"), Is.Empty);
                Assert.That(Maps(), Is.EqualTo(maps));
            });

            RaiseAs(entMan, d, new WFMappingUploadConfirmRequest(question.TransferId, true));
            var versions = Versions(res, d.UserId, "Missing");
            Assert.Multiple(() =>
            {
                Assert.That(upload.LastOutcome(d.UserId), Is.EqualTo("wf-mapping-mode-upload-done"));
                Assert.That(upload.PendingConfirmation(d.UserId), Is.Null);
                Assert.That(versions, Has.Count.EqualTo(1));
                Assert.That(AnyScratch(), Is.False);
                Assert.That(Maps(), Is.EqualTo(maps));
            });

            var stored = res.UserData.ReadAllText(WFMappingLibraryNames.LevelPath(d.UserId, "Missing", versions[0], 0));
            Assert.Multiple(() =>
            {
                Assert.That(stored, Does.Not.Contain(Missing));
                Assert.That(stored, Does.Not.Contain($"proto: {OnWall}"));
                Assert.That(stored, Does.Contain($"proto: {Loose}"));
            });

            // A held upload that nobody confirms in time is dropped.
            server.CfgMan.SetCVar(WFMappingModeCVars.UploadConfirmSeconds, 1f);
            Assert.That(Upload(e, "Expired", missingBytes), Is.EqualTo("wf-mapping-mode-upload-needs-confirm"));
            expired = upload.PendingConfirmation(e.UserId)?.TransferId ?? 0;
            Assert.That(expired, Is.Not.Zero);
        });

        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(upload.PendingConfirmation(e.UserId), Is.Null);
                Assert.That(upload.LastOutcome(e.UserId), Is.EqualTo("wf-mapping-mode-upload-confirm-expired"));
            });

            RaiseAs(entMan, e, new WFMappingUploadConfirmRequest(expired, true));
            Assert.Multiple(() =>
            {
                Assert.That(Versions(res, e.UserId, "Expired"), Is.Empty, "A confirm after the wait stores nothing.");
                Assert.That(upload.LastOutcome(e.UserId), Is.EqualTo("wf-mapping-mode-upload-confirm-expired"));
                Assert.That(AnyScratch(), Is.False);
            });
            server.CfgMan.SetCVar(WFMappingModeCVars.UploadConfirmSeconds, WFMappingModeCVars.UploadConfirmSeconds.DefaultValue);

            // The stored version opens with only the loose item left on its grid.
            RaiseAs(entMan, d, new WFMappingOpenRequest("Missing", null));
            Assert.That(mapping.TryGetPresent(d, out var opened), Is.True);

            var remaining = new List<string>();
            var stack = new Stack<EntityUid>();
            var grids = 0;
            var children = entMan.GetComponent<TransformComponent>(opened!.RootMap).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (!entMan.HasComponent<MapGridComponent>(child))
                    continue;

                grids++;
                stack.Push(child);
            }

            while (stack.TryPop(out var parent))
            {
                var onParent = entMan.GetComponent<TransformComponent>(parent).ChildEnumerator;
                while (onParent.MoveNext(out var entity))
                {
                    if (entMan.HasComponent<WFMappingObserverComponent>(entity))
                        continue;

                    remaining.Add(entMan.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID ?? string.Empty);
                    stack.Push(entity);
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(grids, Is.EqualTo(1));
                Assert.That(remaining, Is.EqualTo(new[] { Loose }), "Only the loose item is left.");
            });

            mapping.LeaveSession(d.UserId, kicked: false);
        });
        await pair.RunTicksSync(5);

        // The request throttle refills.
        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            var maps = Maps();
            var valid = Encoding.UTF8.GetBytes(yaml);

            // The server-wide load slot is taken: refused after the transfer, and the cooldown is given back.
            server.CfgMan.SetCVar(WFMappingModeCVars.UploadCooldown, 30f);
            server.CfgMan.SetCVar(WFMappingModeCVars.ReferenceCooldown, 60f);
            Assert.That(mapping.TryReserveLoad(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(Upload(a, "Load Busy", valid), Is.EqualTo("wf-mapping-mode-upload-load-busy"));
                Assert.That(Versions(res, a.UserId, "Load Busy"), Is.Empty);
                Assert.That(AnyScratch(), Is.False);
                Assert.That(Maps(), Is.EqualTo(maps));
            });

            RaiseAs(entMan, a, new WFMappingUploadBeginRequest("Load Busy", valid.Length, WFMappingUploadAssembler.ChunksFor(valid.Length)));
            Assert.That(upload.ActiveTransfer(a.UserId), Is.Not.Null, "A busy load slot costs no cooldown.");
            RaiseAs(entMan, a, new WFMappingUploadCancelRequest(upload.ActiveTransfer(a.UserId)!.Value));

            server.CfgMan.SetCVar(WFMappingModeCVars.UploadCooldown, WFMappingModeCVars.UploadCooldown.DefaultValue);
            server.CfgMan.SetCVar(WFMappingModeCVars.ReferenceCooldown, WFMappingModeCVars.ReferenceCooldown.DefaultValue);
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
