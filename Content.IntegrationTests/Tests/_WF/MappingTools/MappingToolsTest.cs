#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Placement;

namespace Content.IntegrationTests.Tests._WF.MappingTools;

/// <summary>
/// Moves, rotations, deletes and pastes land where the client preview says, and undo puts everything back.
/// </summary>
[TestFixture]
public sealed class MappingToolsTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFMappingToolsTestFixture
  components:
  - type: Transform
    anchored: true
  - type: ContainerContainer

- type: entity
  id: WFMappingToolsTestItem
";

    [Test]
    public async Task EditsAndUndo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var testMap = await pair.CreateTestMap();

        var mapSys = entMan.System<MapSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var session = pair.Player!;

        var grid = testMap.Grid;
        var plating = new Tile(tileDefs["Plating"].TileId);

        EntityUid FixtureAt(Vector2i cell)
        {
            var anchored = mapSys.GetAnchoredEntitiesEnumerator(grid, grid, cell);
            while (anchored.MoveNext(out var uid))
            {
                if (entMan.GetComponent<MetaDataComponent>(uid.Value).EntityPrototype?.ID == "WFMappingToolsTestFixture")
                    return uid.Value;
            }

            return EntityUid.Invalid;
        }

        bool HasTile(Vector2i cell) => mapSys.TryGetTileRef(grid, grid, cell, out var tile) && !tile.Tile.IsEmpty;

        MappingSelection Area(Box2i box) => new() { Grid = entMan.GetNetEntity(grid), Area = box };

        await server.WaitAssertion(() =>
        {
            // The test tiles sit apart from the test map's own tile.
            grid.Comp.CanSplit = false;
            mapSys.SetTiles(grid, grid, new() { (new Vector2i(10, 0), plating), (new Vector2i(11, 0), plating) });

            var fixture = entMan.SpawnEntity("WFMappingToolsTestFixture", new EntityCoordinates(grid, new Vector2(10.5f, 0.5f)));
            var box = containers.EnsureContainer<Container>(fixture, "wf_test");
            var item = entMan.SpawnEntity("WFMappingToolsTestItem", MapCoordinates.Nullspace);
            containers.Insert(item, box);

            Assert.That(FixtureAt(new Vector2i(10, 0)), Is.Not.EqualTo(EntityUid.Invalid));

            // A cable cuts itself if it is ever unanchored, so a move has to carry it anchored.
            var cable = entMan.SpawnEntity("CableApcExtension", new EntityCoordinates(grid, new Vector2(11.5f, 0.5f)));
            Assert.That(entMan.GetComponent<TransformComponent>(cable).Anchored, Is.True);

            // Move two tiles up.
            tools.Move(session, Area(new Box2i(10, 0, 12, 1)), new Vector2i(0, 2), 0);
            Assert.Multiple(() =>
            {
                Assert.That(HasTile(new Vector2i(10, 0)), Is.False);
                Assert.That(HasTile(new Vector2i(10, 2)), Is.True);
                Assert.That(FixtureAt(new Vector2i(10, 2)), Is.EqualTo(fixture), "The fixture moves, not a copy of it.");
                Assert.That(entMan.Deleted(cable), Is.False, "The cable must not cut itself.");
                Assert.That(entMan.GetComponent<TransformComponent>(cable).Anchored, Is.True);
                Assert.That(mapSys.GetAnchoredEntities(grid, grid, new Vector2i(11, 2)), Does.Contain(cable));
                Assert.That(mapSys.GetAnchoredEntities(grid, grid, new Vector2i(11, 0)), Does.Not.Contain(cable));
            });

            tools.StepHistory(session, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(HasTile(new Vector2i(10, 2)), Is.False);
                Assert.That(HasTile(new Vector2i(10, 0)), Is.True);
                Assert.That(FixtureAt(new Vector2i(10, 0)), Is.EqualTo(fixture));
            });

            // A 2x1 strip turned clockwise stands up in the column of its left cell, as the preview draws it.
            tools.Move(session, Area(new Box2i(10, 0, 12, 1)), Vector2i.Zero, 1);
            var expected = MappingToolsMath.TransformCell(new Vector2i(10, 0), new Box2i(10, 0, 12, 1), Vector2i.Zero, 1);
            Assert.Multiple(() =>
            {
                Assert.That(expected, Is.EqualTo(new Vector2i(10, 0)));
                Assert.That(HasTile(new Vector2i(10, -1)), Is.True);
                Assert.That(HasTile(new Vector2i(11, 0)), Is.False);
                Assert.That(FixtureAt(expected), Is.EqualTo(fixture));
            });
            tools.StepHistory(session, redo: false);

            // Delete, then undo recreates the fixture with its contents.
            tools.Delete(session, Area(new Box2i(10, 0, 12, 1)));
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(fixture), Is.True);
                Assert.That(HasTile(new Vector2i(10, 0)), Is.False);
            });

            tools.StepHistory(session, redo: false);
            var restored = FixtureAt(new Vector2i(10, 0));
            Assert.That(restored, Is.Not.EqualTo(EntityUid.Invalid), "Undo should bring the fixture back, anchored.");
            Assert.Multiple(() =>
            {
                Assert.That(HasTile(new Vector2i(11, 0)), Is.True);
                Assert.That(containers.TryGetContainer(restored, "wf_test", out var restoredBox), Is.True);
                Assert.That(restoredBox!.ContainedEntities, Has.Count.EqualTo(1), "The contents come back too.");
            });

            // Redo deletes the restored fixture, undo brings it back again under yet another uid.
            tools.StepHistory(session, redo: true);
            Assert.That(entMan.Deleted(restored), Is.True);
            tools.StepHistory(session, redo: false);
            restored = FixtureAt(new Vector2i(10, 0));
            Assert.That(restored, Is.Not.EqualTo(EntityUid.Invalid));

            // Copy and paste somewhere else, then undo the paste.
            tools.Copy(session, Area(new Box2i(10, 0, 12, 1)), cut: false);
            tools.Paste(session, new EntityCoordinates(grid, new Vector2(20.5f, 5.5f)), 0);
            var pasted = MappingToolsMath.PasteOrigin(new Vector2i(20, 5), new Vector2i(2, 1), 0);
            Assert.Multiple(() =>
            {
                Assert.That(HasTile(pasted), Is.True);
                Assert.That(HasTile(pasted + new Vector2i(1, 0)), Is.True);
                Assert.That(FixtureAt(pasted), Is.Not.EqualTo(EntityUid.Invalid));
                Assert.That(FixtureAt(new Vector2i(10, 0)), Is.EqualTo(restored), "Copying leaves the original.");
            });

            tools.StepHistory(session, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(HasTile(pasted), Is.False);
                Assert.That(FixtureAt(pasted), Is.EqualTo(EntityUid.Invalid));
            });

            // Moving an entity picked by itself leaves the tiles alone.
            var single = new MappingSelection { Grid = entMan.GetNetEntity(grid) };
            single.Entities.Add(entMan.GetNetEntity(restored));
            tools.Move(session, single, new Vector2i(1, 0), 0);
            Assert.Multiple(() =>
            {
                Assert.That(FixtureAt(new Vector2i(11, 0)), Is.EqualTo(restored));
                Assert.That(HasTile(new Vector2i(10, 0)), Is.True);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlacementMenuUndo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var testMap = await pair.CreateTestMap();

        var mapSys = entMan.System<MapSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var session = pair.Player!;
        var grid = testMap.Grid;

        await server.WaitAssertion(() =>
        {
            // The engine sets the tile, then raises the placement event.
            var cell = new Vector2i(1, 0);
            var coords = new EntityCoordinates(grid, new Vector2(1.5f, 0.5f));
            var plating = tileDefs["Plating"].TileId;
            mapSys.SetTile(grid, grid, cell, new Tile(plating));
            entMan.EventBus.RaiseEvent(EventSource.Local, new PlacementTileEvent(plating, coords, session.UserId));

            // The engine raises the erase event, then deletes.
            var fixture = entMan.SpawnEntity("WFMappingToolsTestFixture", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            entMan.EventBus.RaiseEvent(EventSource.Local,
                new PlacementEntityEvent(fixture, entMan.GetComponent<TransformComponent>(fixture).Coordinates, PlacementEventAction.Erase, session.UserId));
            entMan.DeleteEntity(fixture);

            // Both happened in one tick, so one undo reverts both.
            tools.StepHistory(session, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(mapSys.TryGetTileRef(grid, grid, cell, out var tile) && !tile.Tile.IsEmpty, Is.False);
                var anchored = mapSys.GetAnchoredEntitiesEnumerator(grid, grid, Vector2i.Zero);
                Assert.That(anchored.MoveNext(out var restored), Is.True, "The erased fixture comes back.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(restored!.Value).EntityPrototype?.ID, Is.EqualTo("WFMappingToolsTestFixture"));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HideWalls()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var testMap = await pair.CreateTestMap();

        var wall = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            wall = server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));

            // Maps opened with the mapping command are paused, which some entity queries skip.
            server.EntMan.System<MapSystem>().SetPaused(testMap.MapId, true);
        });
        await pair.RunTicksSync(10);

        var clientTools = client.EntMan.System<Content.Client._WF.MappingTools.MappingToolsSystem>();
        var clientWall = client.EntMan.GetEntity(server.EntMan.GetNetEntity(wall));

        await client.WaitAssertion(() =>
        {
            var sprite = client.EntMan.GetComponent<Robust.Client.GameObjects.SpriteComponent>(clientWall);
            Assert.That(sprite.Visible, Is.True);

            clientTools.SetWallsHidden(true);
            Assert.That(sprite.Visible, Is.False, "Hiding walls hides the wall's sprite.");

            clientTools.SetWallsHidden(false);
            Assert.That(sprite.Visible, Is.True, "Showing walls brings it back.");

            // The keys only exist while the tools are open, so Ctrl+C and the like don't shadow C for other players.
            var common = client.ResolveDependency<Robust.Client.Input.IInputManager>().Contexts.GetContext("common");
            Assert.That(common.FunctionExists(Content.Shared.Input.ContentKeyFunctions.WFMappingCopy), Is.False);
            clientTools.SetEnabled(true);
            Assert.That(common.FunctionExists(Content.Shared.Input.ContentKeyFunctions.WFMappingCopy), Is.True);
            clientTools.SetEnabled(false);
            Assert.That(common.FunctionExists(Content.Shared.Input.ContentKeyFunctions.WFMappingCopy), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task Eyedropper()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var testMap = await pair.CreateTestMap();

        var wall = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            wall = server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(testMap.Grid, new Vector2(0.5f, 0.5f)));
            server.EntMan.System<TransformSystem>().SetLocalRotation(wall, Angle.FromDegrees(90));
            server.EntMan.System<MapSystem>().SetPaused(testMap.MapId, true);
        });
        await pair.RunTicksSync(10);

        var tools = client.EntMan.System<Content.Client._WF.MappingTools.MappingToolsSystem>();
        var placement = client.ResolveDependency<Robust.Client.Placement.IPlacementManager>();
        var clientWall = client.EntMan.GetEntity(server.EntMan.GetNetEntity(wall));
        var clientGrid = client.EntMan.GetEntity(server.EntMan.GetNetEntity(testMap.Grid.Owner));

        await client.WaitAssertion(() =>
        {
            tools.Pick(client.EntMan.GetComponent<TransformComponent>(clientWall).Coordinates, clientWall);
            Assert.Multiple(() =>
            {
                Assert.That(placement.CurrentPermission?.EntityType, Is.EqualTo("WallSolid"));
                Assert.That(placement.Direction, Is.EqualTo(Direction.East), "The pick keeps the wall's rotation.");
            });

            // Nothing under the cursor: pick the floor tile instead.
            placement.Clear();
            tools.Pick(new EntityCoordinates(clientGrid, new Vector2(0.5f, 0.5f)), EntityUid.Invalid);
            Assert.Multiple(() =>
            {
                Assert.That(placement.CurrentPermission?.IsTile, Is.True);
                Assert.That(placement.CurrentPermission?.TileType, Is.EqualTo(testMap.Tile.Tile.TypeId));
            });
            placement.Clear();
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task Mirror()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var mapSys = entMan.System<MapSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var session = pair.Player!;
        var grid = testMap.Grid;

        await server.WaitAssertion(() =>
        {
            grid.Comp.CanSplit = false;
            var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            mapSys.SetTiles(grid, grid, new() { (new Vector2i(10, 0), plating), (new Vector2i(11, 0), plating), (new Vector2i(12, 0), plating) });

            var fixture = entMan.SpawnEntity("WFMappingToolsTestFixture", new EntityCoordinates(grid, new Vector2(10.5f, 0.5f)));

            // A bend is not symmetric: mirrored left-right, its east arm has to become a west arm.
            var bend = entMan.SpawnEntity("GasPipeBend", new EntityCoordinates(grid, new Vector2(12.5f, 0.5f)));
            PipeDirection Connections() => entMan.GetComponent<Content.Shared.NodeContainer.NodeContainerComponent>(bend)
                .Nodes.Values.OfType<Content.Server.NodeContainer.Nodes.PipeNode>().Single().CurrentPipeDirection;
            var before = Connections();

            var selection = new MappingSelection { Grid = entMan.GetNetEntity(grid), Area = new Box2i(10, 0, 13, 1) };
            tools.Mirror(session, selection, vertical: false);

            var after = Connections();
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(fixture).LocalPosition, Is.EqualTo(new Vector2(12.5f, 0.5f)));
                Assert.That(entMan.GetComponent<TransformComponent>(bend).LocalPosition, Is.EqualTo(new Vector2(10.5f, 0.5f)));
                Assert.That(after.HasFlag(PipeDirection.East), Is.EqualTo(before.HasFlag(PipeDirection.West)));
                Assert.That(after.HasFlag(PipeDirection.West), Is.EqualTo(before.HasFlag(PipeDirection.East)));
                Assert.That(after.HasFlag(PipeDirection.North), Is.EqualTo(before.HasFlag(PipeDirection.North)));
                Assert.That(after.HasFlag(PipeDirection.South), Is.EqualTo(before.HasFlag(PipeDirection.South)));
            });

            tools.StepHistory(session, redo: false);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(fixture).LocalPosition, Is.EqualTo(new Vector2(10.5f, 0.5f)));
                Assert.That(Connections(), Is.EqualTo(before));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PasteConstructedEntities()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var mapSys = entMan.System<MapSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var session = pair.Player!;
        var grid = testMap.Grid;

        await server.WaitAssertion(() =>
        {
            grid.Comp.CanSplit = false;
            var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            mapSys.SetTiles(grid, grid, new() { (new Vector2i(10, 0), plating), (new Vector2i(11, 0), plating) });

            // A conveyor's construction node snaps it to the grid on startup, which threw for a pasted one.
            entMan.SpawnEntity("ConveyorBelt", new EntityCoordinates(grid, new Vector2(10.5f, 0.5f)));
            entMan.SpawnEntity("GasPipeStraight", new EntityCoordinates(grid, new Vector2(11.5f, 0.5f)));

            tools.Copy(session, new MappingSelection { Grid = entMan.GetNetEntity(grid), Area = new Box2i(10, 0, 12, 1) }, cut: false);
            tools.Paste(session, new EntityCoordinates(grid, new Vector2(20.5f, 5.5f)), 0);

            var pasted = MappingToolsMath.PasteOrigin(new Vector2i(20, 5), new Vector2i(2, 1), 0);
            string?[] At(Vector2i cell) => mapSys.GetAnchoredEntities(grid, grid, cell)
                .Select(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(At(pasted), Does.Contain("ConveyorBelt"));
                Assert.That(At(pasted + new Vector2i(1, 0)), Does.Contain("GasPipeStraight"));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public void TileStateMath()
    {
        Assert.Multiple(() =>
        {
            for (byte state = 0; state < 8; state++)
            {
                Assert.That(MappingToolsMath.RotateTileState(state, 4), Is.EqualTo(state));
                Assert.That(MappingToolsMath.MirrorTileState(MappingToolsMath.MirrorTileState(state, false), false), Is.EqualTo(state));
                Assert.That(MappingToolsMath.MirrorTileState(MappingToolsMath.MirrorTileState(state, true), true), Is.EqualTo(state));
                // A mirror always toggles the mirrored half of the states.
                Assert.That(MappingToolsMath.MirrorTileState(state, false) >= 4, Is.Not.EqualTo(state >= 4));
            }

            // Facing east mirrors to west left-right; facing south mirrors to north top-bottom.
            Assert.That(MappingToolsMath.MirrorAngle(Direction.East.ToAngle(), false).GetDir(), Is.EqualTo(Direction.West));
            Assert.That(MappingToolsMath.MirrorAngle(Direction.South.ToAngle(), true).GetDir(), Is.EqualTo(Direction.North));
            Assert.That(MappingToolsMath.MirrorAngle(Direction.North.ToAngle(), false).GetDir(), Is.EqualTo(Direction.North));
        });
    }

    [Test]
    public void RotationMath()
    {
        var extent = new Box2i(0, 0, 3, 1);
        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsMath.RotatedSize(extent.Size, 1), Is.EqualTo(new Vector2i(1, 3)));
            // Clockwise: the east end of a row ends up at the bottom of the column.
            Assert.That(MappingToolsMath.TransformCell(new Vector2i(2, 0), extent, Vector2i.Zero, 1), Is.EqualTo(new Vector2i(1, -1)));
            Assert.That(MappingToolsMath.TransformCell(new Vector2i(0, 0), extent, Vector2i.Zero, 1), Is.EqualTo(new Vector2i(1, 1)));
            // Four turns are none.
            Assert.That(MappingToolsMath.TransformCell(new Vector2i(2, 0), extent, Vector2i.Zero, 4), Is.EqualTo(new Vector2i(2, 0)));
            Assert.That(MappingToolsMath.RotationDelta(1), Is.EqualTo(Angle.FromDegrees(-90)));
        });
    }
}
