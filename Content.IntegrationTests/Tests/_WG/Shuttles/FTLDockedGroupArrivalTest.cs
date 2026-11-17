using System.Collections.Generic;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Shuttles;

/// <summary>FTL anti-collision clears room for the grids docked to an arriving shuttle and moves them with it.</summary>
[TestOf(typeof(FTLAntiCollisionSystem))]
public sealed class FTLDockedGroupArrivalTest
{
    [Test]
    public async Task DockedGridMovesClearOfNeighbour()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var xforms = entities.System<SharedTransformSystem>();
        var docking = entities.System<DockingSystem>();

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(testMap.Grid);

            // A three-tile shuttle carrying an 80 m grid, which reaches a neighbour well past the 50 m centre check.
            var shuttle = maps.CreateGridEntity(testMap.MapId);
            maps.SetTiles(shuttle.Owner, shuttle.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(0, 0), new Tile(1)),
                (new Vector2i(0, 1), new Tile(1)),
                (new Vector2i(0, 2), new Tile(1)),
            });
            var shuttleDock = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(shuttle.Owner, new Vector2(0.5f, 0.5f)));

            var carried = maps.CreateGridEntity(testMap.MapId);
            xforms.SetWorldPosition(carried.Owner, new Vector2(0f, -2f));
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 80; x++)
                tiles.Add((new Vector2i(x, 0), new Tile(1)));
            maps.SetTiles(carried.Owner, carried.Comp, tiles);
            var carriedDock = entities.SpawnEntity("AirlockShuttle", new EntityCoordinates(carried.Owner, new Vector2(0.5f, 0.5f)));

            docking.Dock((shuttleDock, entities.GetComponent<DockingComponent>(shuttleDock)),
                (carriedDock, entities.GetComponent<DockingComponent>(carriedDock)));

            var neighbour = maps.CreateGridEntity(testMap.MapId);
            xforms.SetWorldPosition(neighbour.Owner, new Vector2(70f, 3f));
            maps.SetTile(neighbour.Owner, neighbour.Comp, Vector2i.Zero, new Tile(1));

            var offsetBefore = xforms.GetWorldPosition(carried.Owner) - xforms.GetWorldPosition(shuttle.Owner);
            var ev = new FTLCompletedEvent(shuttle.Owner, testMap.MapUid);
            entities.EventBus.RaiseLocalEvent(shuttle.Owner, ref ev, true);

            var offsetAfter = xforms.GetWorldPosition(carried.Owner) - xforms.GetWorldPosition(shuttle.Owner);
            Assert.That(offsetAfter, Is.EqualTo(offsetBefore).Using<Vector2>((a, b) => (a - b).Length() < 0.01f),
                "The docked grid should move with its shuttle.");

            var carriedBounds = xforms.GetWorldMatrix(carried.Owner).TransformBox(carried.Comp.LocalAABB);
            var neighbourBounds = xforms.GetWorldMatrix(neighbour.Owner).TransformBox(neighbour.Comp.LocalAABB);
            Assert.That(carriedBounds.Enlarged(5f).Intersects(neighbourBounds), Is.False,
                "The docked grid should arrive clear of the neighbour.");
        });

        await pair.CleanReturnAsync();
    }
}
