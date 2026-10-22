#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Robust.Server.Console;
using Robust.Server.GameObjects;
using Robust.Server.Placement;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network.Messages;
using Robust.Shared.Player;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// Things that reach past a session (Nar'Sie and anything else with a denied component) are refused at placement,
/// draw and paste and stripped at reference load, upload and library open; one already on the map is erased by click
/// and by area like anything else, and <c>wf_mappingmode_purge</c> removes every instance of a prototype.
/// </summary>
[TestFixture]
public sealed class WFMappingDenyTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFMappingDenyTestItem

- type: entity
  id: WFMappingDenyTestMarker
  components:
  - type: WarpPoint
    location: deny test

- type: entity
  id: WFMappingDenyTestWell
  components:
  - type: GravityWell

- type: entity
  id: WFMappingDenyTestRadio
  components:
  - type: ActiveRadio
    globalReceive: true

- type: entity
  id: WFMappingDenyTestWarp
  components:
  - type: WarpPoint
    follow: true

- type: entity
  id: WFMappingDenyTestRole
  components:
  - type: GhostRole
    name: ghost-role-component-default-rules
    description: ghost-role-component-default-rules

- type: entity
  id: WFMappingDenyTestDespawn
  components:
  - type: TimedDespawn
    lifetime: 1000
  - type: SpawnOnDespawn
    prototype: MobNarsie
";

    private const string Narsie = "MobNarsie";
    private const string NarsieSpawn = "MobNarsieSpawn";

    /// <summary>Denied test prototypes that save, so they can be put in a file.</summary>
    private static readonly string[] SavedDenied =
    {
        "WFMappingDenyTestWell", "WFMappingDenyTestRadio", "WFMappingDenyTestWarp", "WFMappingDenyTestRole",
        "WFMappingDenyTestDespawn",
    };

    [Test]
    public async Task DeniedRefusedStrippedAndErasable()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var library = entMan.System<WFMappingLibrarySystem>();
        var upload = entMan.System<WFMappingUploadSystem>();
        var mapSys = entMan.System<MapSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var placement = server.ResolveDependency<IPlacementManager>();
        var console = server.ResolveDependency<IServerConsoleHost>();
        var res = server.ResolveDependency<IResourceManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = pair.Player!;

        WFMappingSession session = default!;
        Entity<MapGridComponent> grid = default;
        var plating = new Tile(tileDefs["Plating"].TileId);

        NetCoordinates On(float x, float y) => entMan.GetNetCoordinates(new EntityCoordinates(grid, new Vector2(x, y)));

        MsgPlacement Place(string prototype, float x, float y) => new()
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            EntityTemplateName = prototype,
            NetCoordinates = On(x, y),
            Align = "None",
            MsgChannel = a.Channel,
        };

        void EngineErase(EntityUid target) => placement.HandleNetMessage(new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestEntRemove,
            EntityUid = entMan.GetNetEntity(target),
            MsgChannel = a.Channel,
        });

        MappingToolsDrawEvent AreaErase(Box2i area)
        {
            var cells = new List<Vector2i>();
            for (var x = area.Left; x < area.Right; x++)
            {
                for (var y = area.Bottom; y < area.Top; y++)
                {
                    cells.Add(new Vector2i(x, y));
                }
            }

            return new MappingToolsDrawEvent(entMan.GetNetEntity(grid), MappingDrawOp.EraseEntities, cells, null, 0,
                null, Angle.Zero);
        }

        int Count(string prototype) => entMan.EntityQuery<MetaDataComponent>(true)
            .Count(m => m.EntityPrototype?.ID == prototype && !entMan.IsQueuedForDeletion(m.Owner));

        // A map of one grid holding every saved denied prototype, a plain warp marker and an item.
        EntityUid DeniedMap()
        {
            var map = mapSys.CreateMap(out var mapId, runMapInit: false);
            var mapGrid = mapSys.CreateGridEntity(mapId);
            mapSys.SetTiles(mapGrid, mapGrid.Comp,
                Enumerable.Range(0, 8).Select(x => (new Vector2i(x, 0), plating)).ToList());
            var x = 0.5f;
            foreach (var prototype in SavedDenied.Append("WFMappingDenyTestMarker").Append("WFMappingDenyTestItem"))
            {
                entMan.SpawnEntity(prototype, new EntityCoordinates(mapGrid, new Vector2(x++, 0.5f)));
            }

            return map;
        }

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

            return upload.LastOutcome(player.UserId);
        }

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(WFMappingModeCVars.UploadCooldown, 0f);
            server.CfgMan.SetCVar(WFMappingModeCVars.ReferenceCooldown, 0f);
            Assert.That(mapping.TryCreateSession(a, "Deny Test", out var created), Is.True);
            session = created!;
            grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -4; x < 4; x++)
            {
                for (var y = -4; y < 4; y++)
                {
                    tiles.Add((new Vector2i(x, y), plating));
                }
            }

            mapSys.SetTiles(grid, grid.Comp, tiles);
        });

        // Placement: Nar'Sie, its spawn animation and every denied component are refused, through the gate and
        // through the engine's own handler; a plain warp marker is still allowed.
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var prototype in SavedDenied.Append(Narsie).Append(NarsieSpawn))
                {
                    Assert.That(mapping.AllowPlacement(a, Place(prototype, 0.5f, 0.5f)), Is.False, prototype);
                }

                Assert.That(mapping.AllowPlacement(a, Place("WFMappingDenyTestMarker", 0.5f, 0.5f)), Is.True);
            });

            placement.HandleNetMessage(Place(Narsie, 0.5f, 0.5f));
            placement.HandleNetMessage(Place(NarsieSpawn, 1.5f, 0.5f));
            Assert.Multiple(() =>
            {
                Assert.That(Count(Narsie), Is.Zero, "Nar'Sie isn't placed.");
                Assert.That(Count(NarsieSpawn), Is.Zero);
            });

            // Shape and fill draws are refused too.
            tools.Draw(a, new MappingToolsDrawEvent(entMan.GetNetEntity(grid), MappingDrawOp.SpawnEntities,
                new List<Vector2i> { new(0, 0) }, null, 0, Narsie, Angle.Zero));
            Assert.That(Count(Narsie), Is.Zero, "Nar'Sie isn't drawn.");
        });

        // Paste: a clipboard carrying a denied entity (already on the map) is refused.
        await server.WaitAssertion(() =>
        {
            var well = entMan.SpawnEntity("WFMappingDenyTestWell", new EntityCoordinates(grid, new Vector2(2.5f, 2.5f)));
            tools.Copy(a, new MappingSelection { Grid = entMan.GetNetEntity(grid), Area = new Box2i(2, 2, 3, 3) }, cut: false);
            tools.Paste(a, new EntityCoordinates(grid, new Vector2(-2.5f, -2.5f)), 0);
            Assert.That(Count("WFMappingDenyTestWell"), Is.EqualTo(1), "The paste is refused.");
            entMan.DeleteEntity(well);
        });

        // Reference load: the denied entities are stripped from the file before it is offered.
        await server.WaitAssertion(() =>
        {
            var source = DeniedMap();
            var version = new WFMappingVersionName(1, new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc),
                WFMappingVersionKind.Manual).Folder;
            var path = WFMappingLibraryNames.LevelPath(a.UserId, "Denied Source", version, 0);
            res.UserData.CreateDir(path.Directory);
            Assert.That(loader.TrySaveMap(source, path), Is.True);
            entMan.DeleteEntity(source);

            Assert.That(mapping.TryLoadOntoScratch(path, a, session, false, "wf-mapping-mode-reference-failed",
                out var load, out var refusal), Is.True, refusal.Key);
            Assert.Multiple(() =>
            {
                Assert.That(load!.Stripped, Is.EqualTo(SavedDenied.Length));
                foreach (var prototype in SavedDenied)
                {
                    Assert.That(Count(prototype), Is.Zero, $"{prototype} is stripped from the reference.");
                }

                Assert.That(Count("WFMappingDenyTestMarker"), Is.EqualTo(1), "A plain warp marker stays.");
                Assert.That(Count("WFMappingDenyTestItem"), Is.EqualTo(1));
            });

            entMan.DeleteEntity(load!.Map);
        });

        // Upload: stored without the denied entities.
        await server.WaitAssertion(() =>
        {
            var source = DeniedMap();
            using var writer = new StringWriter();
            Assert.That(loader.TrySaveMap(source, writer), Is.True);
            entMan.DeleteEntity(source);
            var yaml = writer.ToString();
            Assert.That(yaml, Does.Contain("WFMappingDenyTestWell"));

            Assert.That(Upload(a, "Denied Upload", Encoding.UTF8.GetBytes(yaml)), Is.EqualTo("wf-mapping-mode-upload-done"));
            Assert.That(library.TryFindVersion(a.UserId, "Denied Upload", null, out _, out var listed), Is.True);
            var stored = res.UserData.ReadAllText(WFMappingLibraryNames.LevelPath(a.UserId, "Denied Upload", listed!.Name.Folder, 0));
            Assert.Multiple(() =>
            {
                foreach (var prototype in SavedDenied)
                {
                    Assert.That(stored, Does.Not.Contain(prototype), $"{prototype} is stripped from the upload.");
                }

                Assert.That(stored, Does.Contain("WFMappingDenyTestMarker"));
            });
        });

        // Already there (spawned past every check): erased by the engine's click erase and by area, like anything
        // else, and undoing that brings nothing back.
        await server.WaitAssertion(() =>
        {
            foreach (var prototype in new[] { Narsie, NarsieSpawn })
            {
                var clicked = entMan.SpawnEntity(prototype, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
                EngineErase(clicked);
                Assert.That(entMan.Deleted(clicked), Is.True, $"{prototype} is erased by a click.");

                var area = entMan.SpawnEntity(prototype, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
                tools.Draw(a, AreaErase(new Box2i(-1, -1, 2, 2)));
                Assert.That(entMan.Deleted(area), Is.True, $"{prototype} is erased by area.");

                var selected = entMan.SpawnEntity(prototype, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
                tools.Delete(a, new MappingSelection { Grid = entMan.GetNetEntity(grid), Area = new Box2i(0, 0, 1, 1) });
                Assert.That(entMan.Deleted(selected), Is.True, $"{prototype} is deleted by a selection.");
                tools.StepHistory(a, redo: false);
            }

            Assert.That(Count(Narsie), Is.Zero, "An erased Nar'Sie never saves, so undo doesn't bring it back.");
        });

        // Purge: every Nar'Sie on the session's maps goes, wherever it sits; other things and observers stay.
        await server.WaitAssertion(() =>
        {
            entMan.SpawnEntity(Narsie, new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
            entMan.SpawnEntity(Narsie, new EntityCoordinates(session.RootMap, new Vector2(40f, 40f)));
            var holder = entMan.SpawnEntity("WFMappingDenyTestItem", new EntityCoordinates(grid, new Vector2(-1.5f, 1.5f)));
            entMan.SpawnEntity(Narsie, new EntityCoordinates(holder, Vector2.Zero));
            entMan.SpawnEntity("WFMappingDenyTestWell", new EntityCoordinates(grid, new Vector2(2.5f, 1.5f)));
            Assert.That(Count(Narsie), Is.EqualTo(3));

            console.ExecuteCommand(null, $"wf_mappingmode_purge {session.Code} {Narsie}");
            console.ExecuteCommand(null, $"wf_mappingmode_purge {session.Code} {WFMappingModeSystem.ObserverPrototype}");
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Count(Narsie), Is.Zero, "Every Nar'Sie is purged.");
                Assert.That(Count("WFMappingDenyTestWell"), Is.EqualTo(1), "Other prototypes stay.");
                Assert.That(a.AttachedEntity is { } observer && !entMan.Deleted(observer), Is.True,
                    "A purge never takes an observer.");
            });

            console.ExecuteCommand(null, $"wf_mappingmode_purge {session.Code} WFMappingDenyTestWell");
            Assert.That(Count("WFMappingDenyTestWell"), Is.Zero);
        });

        // Library open: a stored version carrying denied entities opens without them. Opening starts from the lobby.
        await server.WaitPost(() => mapping.LeaveSession(a.UserId, kicked: false));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var source = DeniedMap();
            var version = new WFMappingVersionName(1, new DateTime(2026, 10, 19, 13, 0, 0, DateTimeKind.Utc),
                WFMappingVersionKind.Manual).Folder;
            var path = WFMappingLibraryNames.LevelPath(a.UserId, "Denied Open", version, 0);
            res.UserData.CreateDir(path.Directory);
            Assert.That(loader.TrySaveMap(source, path), Is.True);
            entMan.DeleteEntity(source);

            RaiseAs(entMan, a, new WFMappingOpenRequest("Denied Open", null));
            Assert.That(mapping.TryGetPresent(a, out var opened), Is.True, "The version opens as a session.");
            Assert.That(opened, Is.Not.SameAs(session));
            Assert.Multiple(() =>
            {
                foreach (var prototype in SavedDenied)
                {
                    Assert.That(Count(prototype), Is.Zero, $"{prototype} is stripped when the version opens.");
                }

                Assert.That(Count("WFMappingDenyTestMarker"), Is.EqualTo(1));
            });
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
