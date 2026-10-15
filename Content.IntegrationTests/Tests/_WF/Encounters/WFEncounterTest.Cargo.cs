#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared.Doors.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    private static readonly Vector2i[] Sides = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };

    /// <summary>The deck a crewman can walk on a grid, doors included.</summary>
    private HashSet<Vector2i> Walkable(EntityUid grid)
    {
        var maps = SEntMan.System<SharedMapSystem>();
        var planner = Server.System<WFCrewPlannerSystem>();
        var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
        var walkable = new HashSet<Vector2i>();
        var tiles = maps.GetAllTilesEnumerator(grid, gridComp);
        while (tiles.MoveNext(out var tile))
        {
            if (planner.IsStandable(grid, tile.Value.GridIndices, gridComp))
                walkable.Add(tile.Value.GridIndices);
        }

        var doors = SEntMan.EntityQueryEnumerator<DoorComponent, TransformComponent>();
        while (doors.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                walkable.Add(maps.TileIndicesFor(grid, gridComp, xform.Coordinates));
        }

        return walkable;
    }

    private static int Regions(HashSet<Vector2i> walkable)
    {
        var seen = new HashSet<Vector2i>();
        var regions = 0;
        foreach (var start in walkable)
        {
            if (!seen.Add(start))
                continue;

            regions++;
            var frontier = new Queue<Vector2i>();
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                var at = frontier.Dequeue();
                foreach (var side in Sides)
                {
                    if (walkable.Contains(at + side) && seen.Add(at + side))
                        frontier.Enqueue(at + side);
                }
            }
        }

        return regions;
    }

    /// <summary>However much cargo a hull takes, no crate splits its deck or stands against a door.</summary>
    [TestCase(Hub)]
    [TestCase("WFTestFuelSmallHub")]
    public async Task CargoNeverWallsOffARoute(string id)
    {
        await Server.WaitAssertion(() =>
        {
            var encounter = SpawnEncounter(id, new Vector2(-49000, -49000));
            var grid = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["hub"].Grid;

            // Crew stand on deck too; take them off so only the hull's own fittings count.
            var crew = SEntMan.EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
            while (crew.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    SEntMan.DeleteEntity(uid);
            }

            var walkable = Walkable(grid);
            var regions = Regions(walkable);
            var maps = SEntMan.System<SharedMapSystem>();
            var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
            var cargo = Server.System<WFCrewPlannerSystem>().CargoTiles(grid, 40);
            Assert.That(cargo, Is.Not.Empty, "The hull takes some cargo.");

            var doorTiles = new List<Vector2i>();
            var doors = SEntMan.EntityQueryEnumerator<DoorComponent, TransformComponent>();
            while (doors.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid == grid && xform.Anchored)
                    doorTiles.Add(maps.TileIndicesFor(grid, gridComp, xform.Coordinates));
            }

            foreach (var at in cargo)
            {
                var tile = maps.TileIndicesFor(grid, gridComp, at);
                Assert.That(doorTiles.TrueForAll(door => Math.Max(Math.Abs(door.X - tile.X), Math.Abs(door.Y - tile.Y)) > 1),
                    $"Crate at {tile} stands beside a door.");
                walkable.Remove(tile);
            }

            Assert.That(Regions(walkable), Is.EqualTo(regions), $"{cargo.Count} crates leave the deck joined up.");
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }
}
