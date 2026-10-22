#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingTools;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network.Messages;
using Robust.Shared.Placement;
using Robust.Shared.Player;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// A mapper places, erases and edits only on their own session's maps, never erases players or things off the map,
/// can't spawn hidden prototypes, and the backstop takes back what slips past.
/// </summary>
[TestFixture]
public sealed class WFMappingPermissionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFMappingModeTestItem

- type: entity
  id: WFMappingModeTestHidden
  categories: [ HideSpawnMenu ]

- type: entity
  id: WFMappingModeTestAbstract
  abstract: true
";

    private static MsgPlacement Place(NetCoordinates coords, string prototype) => new()
    {
        PlaceType = PlacementManagerMessage.RequestPlacement,
        EntityTemplateName = prototype,
        NetCoordinates = coords,
        Align = "None",
    };

    private static MsgPlacement Erase(NetEntity target) => new()
    {
        PlaceType = PlacementManagerMessage.RequestEntRemove,
        EntityUid = target,
    };

    [Test]
    public async Task OnlyTheirOwnMaps()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var players = await AddMappers(pair, 3);
        var (a, b, c) = (players[0], players[1], players[2]);

        WFMappingSession own = default!;
        WFMappingSession other = default!;
        Entity<MapGridComponent> ownGrid = default;
        Entity<MapGridComponent> otherGrid = default;
        var plating = new Tile(tileDefs["Plating"].TileId);

        bool HasTile(Entity<MapGridComponent> grid, int x, int y) =>
            mapSys.TryGetTileRef(grid, grid, new Vector2i(x, y), out var tile) && !tile.Tile.IsEmpty;

        NetCoordinates On(Entity<MapGridComponent> grid, float x, float y) =>
            entMan.GetNetCoordinates(new EntityCoordinates(grid, new Vector2(x, y)));

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Own Session", out var ownSession), Is.True);
            Assert.That(mapping.TryCreateSession(b, "Other Session", out var otherSession), Is.True);
            own = ownSession!;
            other = otherSession!;
            Assert.That(mapping.TryJoinSession(c, own), Is.True);

            ownGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(own.RootMap).MapId);
            otherGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(other.RootMap).MapId);
            foreach (var grid in new[] { ownGrid, otherGrid })
            {
                mapSys.SetTiles(grid, grid, new() { (new Vector2i(0, 0), plating), (new Vector2i(1, 0), plating) });
            }
        });

        await server.WaitAssertion(() =>
        {
            // Placement: their own map yes, another session's no, and hidden or abstract prototypes never.
            Assert.Multiple(() =>
            {
                Assert.That(mapping.AllowPlacement(a, Place(On(ownGrid, 0.5f, 0.5f), "WFMappingModeTestItem")), Is.True);
                Assert.That(mapping.AllowPlacement(a, Place(On(otherGrid, 0.5f, 0.5f), "WFMappingModeTestItem")), Is.False);
                Assert.That(mapping.AllowPlacement(a, Place(On(ownGrid, 0.5f, 0.5f), "WFMappingModeTestHidden")), Is.False);
                Assert.That(mapping.AllowPlacement(a, Place(On(ownGrid, 0.5f, 0.5f), "WFMappingModeTestAbstract")), Is.False);
                Assert.That(mapping.AllowPlacement(a, Place(On(ownGrid, 0.5f, 0.5f), "WFMappingModeNoSuchPrototype")), Is.False);
            });

            // Erasing: things on their map yes; other maps, nullspace, players and maps no.
            var item = entMan.SpawnEntity("WFMappingModeTestItem", new EntityCoordinates(ownGrid, new Vector2(1.5f, 0.5f)));
            var foreign = entMan.SpawnEntity("WFMappingModeTestItem", new EntityCoordinates(otherGrid, new Vector2(1.5f, 0.5f)));
            var nowhere = entMan.SpawnEntity("WFMappingModeTestItem", MapCoordinates.Nullspace);
            Assert.Multiple(() =>
            {
                Assert.That(mapping.AllowPlacement(a, Erase(entMan.GetNetEntity(item))), Is.True);
                Assert.That(mapping.AllowPlacement(a, Erase(entMan.GetNetEntity(foreign))), Is.False);
                Assert.That(mapping.AllowPlacement(a, Erase(entMan.GetNetEntity(nowhere))), Is.False);
                Assert.That(mapping.AllowPlacement(a, Erase(entMan.GetNetEntity(c.AttachedEntity!.Value))), Is.False,
                    "Another mapper's observer can't be erased.");
                Assert.That(mapping.AllowPlacement(a, Erase(entMan.GetNetEntity(a.AttachedEntity!.Value))), Is.False);
                Assert.That(mapping.AllowPlacement(a, Erase(entMan.GetNetEntity(own.RootMap))), Is.False);
                Assert.That(mapping.AllowPlacement(a, Erase(entMan.GetNetEntity(b.AttachedEntity!.Value))), Is.False);
            });

            // Someone not in a session places nothing.
            entMan.DeleteEntity(nowhere);
        });

        await server.WaitAssertion(() =>
        {
            var area = new MappingSelection { Grid = entMan.GetNetEntity(otherGrid), Area = new Box2i(0, 0, 2, 1) };

            // The edit check refuses a move on another session's grid, and says why.
            var check = new WFMappingCanEditEvent(a, other.RootMap, WFMappingEditKind.Edit) { Grid = otherGrid.Owner, Quiet = true };
            entMan.EventBus.RaiseEvent(EventSource.Local, check);
            Assert.That(check.Cancelled, Is.True);
            Assert.That(check.Reason, Is.EqualTo("wf-mapping-mode-not-your-map"));

            tools.Move(a, area, new Vector2i(0, 2), 0);
            Assert.Multiple(() =>
            {
                Assert.That(HasTile(otherGrid, 0, 0), Is.True, "Another session's grid is left alone.");
                Assert.That(HasTile(otherGrid, 0, 2), Is.False);
            });

            // The same move on their own grid goes through.
            tools.Move(a, new MappingSelection { Grid = entMan.GetNetEntity(ownGrid), Area = new Box2i(0, 0, 2, 1) },
                new Vector2i(0, 2), 0);
            Assert.Multiple(() =>
            {
                Assert.That(HasTile(ownGrid, 0, 0), Is.False);
                Assert.That(HasTile(ownGrid, 0, 2), Is.True);
            });
        });

        // The backstop takes back an entity made on another session's map, and a tile laid there.
        EntityUid forced = default;
        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(otherGrid, new Vector2(0.5f, 0.5f));
            forced = entMan.SpawnEntity("WFMappingModeTestItem", coords);
            entMan.EventBus.RaiseEvent(EventSource.Local,
                new PlacementEntityEvent(forced, coords, PlacementEventAction.Create, a.UserId));

            var cell = new Vector2i(5, 5);
            mapSys.SetTile(otherGrid, otherGrid, cell, plating);
            entMan.EventBus.RaiseEvent(EventSource.Local,
                new PlacementTileEvent(plating.TypeId, new EntityCoordinates(otherGrid, new Vector2(5.5f, 5.5f)), a.UserId));
            Assert.That(HasTile(otherGrid, 5, 5), Is.False, "The tile is put back as it was.");
        });

        await pair.RunTicksSync(3);
        await server.WaitAssertion(() => Assert.That(entMan.Deleted(forced), Is.True));

        // A placement that changes nothing (an empty tile on an empty cell of their own grid) reverts nothing, not
        // even the tile another session laid just before it in the same tick. Tiles join the hull, so nothing splits.
        await server.WaitAssertion(() =>
        {
            mapSys.SetTile(otherGrid, otherGrid, new Vector2i(2, 0), plating);
            entMan.EventBus.RaiseEvent(EventSource.Local,
                new PlacementTileEvent(0, new EntityCoordinates(ownGrid, new Vector2(7.5f, 7.5f)), a.UserId));
            Assert.That(HasTile(otherGrid, 2, 0), Is.True, "Another session's tile stays.");

            // Nor one laid on another cell of their own grid.
            mapSys.SetTile(ownGrid, ownGrid, new Vector2i(2, 2), plating);
            entMan.EventBus.RaiseEvent(EventSource.Local,
                new PlacementTileEvent(plating.TypeId, new EntityCoordinates(ownGrid, new Vector2(0.5f, 2.5f)), a.UserId));
            Assert.That(HasTile(ownGrid, 2, 2), Is.True);
        });

        // A change from an earlier tick is never claimed.
        await server.WaitPost(() => mapSys.SetTile(otherGrid, otherGrid, new Vector2i(3, 0), plating));
        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
        {
            entMan.EventBus.RaiseEvent(EventSource.Local,
                new PlacementTileEvent(plating.TypeId, new EntityCoordinates(otherGrid, new Vector2(3.5f, 0.5f)), a.UserId));
            Assert.That(HasTile(otherGrid, 3, 0), Is.True);
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
