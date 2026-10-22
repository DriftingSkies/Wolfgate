#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Robust.Server.GameObjects;
using Robust.Server.Placement;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network.Messages;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// A mapper never deletes, moves, copies, mirrors or turns another member's observer, an admin ghost, an observer
/// left without its player, a body holding a mind, or anything under one, through any edit path; area erase and grid
/// deletes go round them.
/// </summary>
[TestFixture]
public sealed class WFMappingProtectionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFMappingProtectTestItem

- type: entity
  id: WFMappingProtectTestBody
  save: false
  components:
  - type: MindContainer
";

    private const string Wall = "WallSolid";

    [Test]
    public async Task OthersAreNeverEdited()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var zLevels = entMan.System<MappingZLevelsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var xformSys = entMan.System<TransformSystem>();
        var mindSys = entMan.System<SharedMindSystem>();
        var placement = server.ResolveDependency<IPlacementManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);

        // The test client sends engine placement messages, so it is mapper A; B is a dummy.
        var a = pair.Player!;
        var b = (await AddMappers(pair, 1))[0];

        WFMappingSession session = default!;
        Entity<MapGridComponent> grid = default;
        var targets = new List<EntityUid>();
        EntityUid bObserver = default;
        EntityUid adminGhost = default;
        EntityUid detached = default;
        EntityUid body = default;
        var plating = new Tile(tileDefs["Plating"].TileId);
        var everything = new Box2i(-5, -5, 5, 5);

        void Send(MsgPlacement msg)
        {
            msg.MsgChannel = a.Channel;
            placement.HandleNetMessage(msg);
        }

        MappingSelection Select(EntityUid uid)
        {
            var selection = new MappingSelection { Grid = entMan.GetNetEntity(grid) };
            selection.Entities.Add(entMan.GetNetEntity(uid));
            return selection;
        }

        MappingSelection Area(Box2i area) => new() { Grid = entMan.GetNetEntity(grid), Area = area };

        MappingToolsDrawEvent Draw(MappingDrawOp op, Box2i area, string? tile = null)
        {
            var cells = new List<Vector2i>();
            for (var x = area.Left; x < area.Right; x++)
            {
                for (var y = area.Bottom; y < area.Top; y++)
                {
                    cells.Add(new Vector2i(x, y));
                }
            }

            return new MappingToolsDrawEvent(entMan.GetNetEntity(grid), op, cells, tile, 0, null, Angle.Zero);
        }

        int Count(string prototype) => entMan.EntityQuery<MetaDataComponent>(true)
            .Count(m => m.EntityPrototype?.ID == prototype && !entMan.IsQueuedForDeletion(m.Owner));

        int Protected() => entMan.EntityQuery<WFMappingObserverComponent>(true).Count() +
                           entMan.EntityQuery<GhostComponent>(true).Count();

        Vector2 At(EntityUid uid) => xformSys.GetWorldPosition(uid);

        void AssertKept(string path)
        {
            Assert.Multiple(() =>
            {
                foreach (var target in targets)
                {
                    Assert.That(entMan.Deleted(target) || entMan.IsQueuedForDeletion(target), Is.False,
                        $"{entMan.ToPrettyString(target)} should survive {path}.");
                }
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Protect Test", out var created), Is.True);
            session = created!;
            Assert.That(mapping.TryJoinSession(b, session), Is.True);

            grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = everything.Left; x < everything.Right; x++)
            {
                for (var y = everything.Bottom; y < everything.Top; y++)
                {
                    tiles.Add((new Vector2i(x, y), plating));
                }
            }

            mapSys.SetTiles(grid, grid.Comp, tiles);
            for (var x = -5; x < 0; x++)
            {
                entMan.SpawnEntity(Wall, new EntityCoordinates(grid, new Vector2(x + 0.5f, -4.5f)));
            }

            bObserver = b.AttachedEntity!.Value;
            xformSys.SetCoordinates(bObserver, new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
            adminGhost = entMan.SpawnEntity("AdminObserver", new EntityCoordinates(grid, new Vector2(2.5f, 1.5f)));
            detached = entMan.SpawnEntity(WFMappingModeSystem.ObserverPrototype,
                new EntityCoordinates(grid, new Vector2(3.5f, 1.5f)));
            var carried = entMan.SpawnEntity("WFMappingProtectTestItem", new EntityCoordinates(bObserver, Vector2.Zero));
            body = entMan.SpawnEntity("WFMappingProtectTestBody", new EntityCoordinates(grid, new Vector2(-2.5f, 1.5f)));
            var mind = mindSys.CreateMind(null);
            mindSys.TransferTo(mind, body);
            targets.AddRange(new[] { bObserver, adminGhost, detached, carried, body });
            Assert.That(Count(Wall), Is.EqualTo(5));
        });

        // Engine erase, one entity at a time: refused for every target; an ordinary item still goes.
        await server.WaitAssertion(() =>
        {
            foreach (var target in targets)
            {
                Send(new MsgPlacement { PlaceType = PlacementManagerMessage.RequestEntRemove, EntityUid = entMan.GetNetEntity(target) });
            }

            var item = entMan.SpawnEntity("WFMappingProtectTestItem", new EntityCoordinates(grid, new Vector2(0.5f, -2.5f)));
            Send(new MsgPlacement { PlaceType = PlacementManagerMessage.RequestEntRemove, EntityUid = entMan.GetNetEntity(item) });
            Assert.That(entMan.Deleted(item), Is.True, "An ordinary entity is erased.");

            // The engine's area erase is refused for mappers outright.
            Send(new MsgPlacement
            {
                PlaceType = PlacementManagerMessage.RequestRectRemove,
                NetCoordinates = entMan.GetNetCoordinates(new EntityCoordinates(grid, Vector2.Zero)),
                RectSize = new Vector2(9.9f, 9.9f),
            });
            Assert.That(Count(Wall), Is.EqualTo(5), "The engine area erase does nothing.");
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => AssertKept("engine erase"));

        // Mapping tools: delete, cut, move, turn and mirror, picked singly and by area.
        await server.WaitAssertion(() =>
        {
            var before = targets.ToDictionary(t => t, At);
            foreach (var target in targets)
            {
                var cell = MappingToolsMath.CellOf(xformSys.GetWorldPosition(target));
                var cellArea = new Box2i(cell, cell + Vector2i.One);

                // The area edits take the tile under the target, which is put back each time.
                tools.Delete(a, Select(target));
                tools.Delete(a, Area(cellArea));
                tools.StepHistory(a, redo: false);
                tools.Copy(a, Select(target), cut: true);
                tools.Move(a, Select(target), new Vector2i(0, 1), 0);
                tools.Move(a, Select(target), Vector2i.Zero, 1);
                tools.Move(a, Area(cellArea), new Vector2i(1, 0), 0);
                tools.StepHistory(a, redo: false);
                tools.Move(a, Area(cellArea), Vector2i.Zero, 1);
                tools.StepHistory(a, redo: false);
                tools.Mirror(a, Area(new Box2i(cell - new Vector2i(1, 0), cell + Vector2i.One)), false);
                tools.Mirror(a, Area(new Box2i(cell - new Vector2i(0, 1), cell + Vector2i.One)), true);
            }

            AssertKept("tools edits");
            Assert.Multiple(() =>
            {
                foreach (var target in targets)
                {
                    Assert.That(Vector2.Distance(At(target), before[target]), Is.LessThan(0.01f),
                        $"{entMan.ToPrettyString(target)} shouldn't move.");
                }
            });
        });

        // Copy and paste carry the walls but none of the targets.
        await server.WaitAssertion(() =>
        {
            var protectedBefore = Protected();
            var bodies = Count("WFMappingProtectTestBody");
            var items = Count("WFMappingProtectTestItem");
            tools.Copy(a, Area(new Box2i(-5, -5, 5, 2)), cut: false);
            tools.Paste(a, new EntityCoordinates(grid, new Vector2(0.5f, 20.5f)), 0);
            Assert.Multiple(() =>
            {
                Assert.That(Count(Wall), Is.EqualTo(10), "The walls are pasted.");
                Assert.That(Protected(), Is.EqualTo(protectedBefore), "No observer or ghost is copied.");
                Assert.That(Count("WFMappingProtectTestBody"), Is.EqualTo(bodies));
                Assert.That(Count("WFMappingProtectTestItem"), Is.EqualTo(items));
            });

            tools.StepHistory(a, redo: false);
            Assert.That(Count(Wall), Is.EqualTo(5));
        });

        // Erase mode's area erase takes the walls and leaves every target; undo brings the walls back.
        await server.WaitAssertion(() =>
        {
            tools.Draw(a, Draw(MappingDrawOp.EraseEntities, everything));
            Assert.That(Count(Wall), Is.EqualTo(0), "The area erase removes the walls.");
            AssertKept("area erase");

            tools.StepHistory(a, redo: false);
            Assert.That(Count(Wall), Is.EqualTo(5), "Undo restores the walls.");
            AssertKept("area erase undo");
        });

        // Undoing a placement that an admin ghost has come to sit under moves the ghost off before the delete.
        await server.WaitAssertion(() =>
        {
            tools.Draw(a, new MappingToolsDrawEvent(entMan.GetNetEntity(grid), MappingDrawOp.SpawnEntities,
                new List<Vector2i> { new(0, 3) }, null, 0, "WFMappingProtectTestItem", Angle.Zero));
            var placed = entMan.EntityQuery<MetaDataComponent>(true)
                .Single(m => m.EntityPrototype?.ID == "WFMappingProtectTestItem" &&
                             entMan.GetComponent<TransformComponent>(m.Owner).ParentUid == grid.Owner).Owner;
            xformSys.SetCoordinates(adminGhost, new EntityCoordinates(placed, Vector2.Zero));

            tools.StepHistory(a, redo: false);
            Assert.That(entMan.Deleted(placed), Is.True, "Undo removes the placed item.");
            AssertKept("undo");
            Assert.That(entMan.GetComponent<TransformComponent>(adminGhost).ParentUid, Is.EqualTo(grid.Owner));
        });

        // A z-level removed with an observer and a body on it hands them to the next level.
        EntityUid added = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(zLevels.TryAddLevel(session.RootMap, true, out added, out _, out var error), Is.True, error);
            var addedId = entMan.GetComponent<MapComponent>(added).MapId;
            xformSys.SetMapCoordinates(detached, new MapCoordinates(new Vector2(3.5f, 1.5f), addedId));
            xformSys.SetMapCoordinates(body, new MapCoordinates(new Vector2(-2.5f, 1.5f), addedId));
            Assert.That(zLevels.TryRemoveLevel(added, out error), Is.True, error);
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(added), Is.True);
            AssertKept("z-level removal");
            Assert.That(entMan.GetComponent<TransformComponent>(detached).MapUid, Is.EqualTo(session.RootMap));
            xformSys.SetCoordinates(detached, new EntityCoordinates(grid, new Vector2(3.5f, 1.5f)));
            xformSys.SetCoordinates(body, new EntityCoordinates(grid, new Vector2(-2.5f, 1.5f)));
        });

        // Deleting the whole grid, by selection or by erasing every tile, moves every target off it first.
        await server.WaitAssertion(() =>
        {
            tools.Delete(a, Area(everything));
            Assert.That(entMan.Deleted(grid), Is.True, "The grid is deleted.");
            AssertKept("grid delete");
            Assert.Multiple(() =>
            {
                foreach (var target in targets)
                {
                    Assert.That(entMan.GetComponent<TransformComponent>(target).MapUid, Is.EqualTo(session.RootMap));
                }
            });

            tools.StepHistory(a, redo: false);
            var restored = entMan.EntityQuery<MapGridComponent>(true)
                .Single(g => entMan.GetComponent<TransformComponent>(g.Owner).MapUid == session.RootMap);
            grid = (restored.Owner, restored);
            Assert.That(Count(Wall), Is.EqualTo(5), "Undo brings the grid back with its walls.");
            foreach (var target in new[] { adminGhost, detached, body })
            {
                xformSys.SetCoordinates(target, new EntityCoordinates(grid, xformSys.GetWorldPosition(target)));
            }

            tools.Draw(a, Draw(MappingDrawOp.SetTiles, everything, ContentTileDefinition.SpaceID));
            Assert.That(entMan.Deleted(grid), Is.True, "Erasing every tile deletes the grid.");
            AssertKept("tile erase");
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
