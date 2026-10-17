using System.Collections.Generic;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>
/// FTLToDock, used by the bus, arrivals and evac, runs a full jump and docks on arrival.
/// </summary>
[TestOf(typeof(ShuttleSystem))]
public sealed class FTLToDockTest
{
    private const float StartupTime = 1f;
    private const float TravelSeconds = 2f;
    private const int MaxSeconds = 30;

    [Test]
    public async Task JumpsThenDocks()
    {
        await RunJump(withNeighbour: false);
    }

    /// <summary>
    /// A grid parked near the stop must not make FTL anti-collision push the docked shuttle away, which drags the stop along.
    /// </summary>
    [Test]
    public async Task NearbyGridLeavesShuttleDocked()
    {
        await RunJump(withNeighbour: true);
    }

    private static async Task RunJump(bool withNeighbour)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var sourceMap = await pair.CreateTestMap();
        var targetMap = await pair.CreateTestMap();

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.System<SharedMapSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();
        var shuttleSystem = entManager.System<ShuttleSystem>();

        var shuttle = EntityUid.Invalid;
        var target = EntityUid.Invalid;
        var shuttleDock = EntityUid.Invalid;
        var targetDock = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            entManager.DeleteEntity(sourceMap.Grid);
            entManager.DeleteEntity(targetMap.Grid);
            Assert.That(shuttleSystem.TryAddFTLDestination(targetMap.MapId, true, out _));

            // Same shapes as upstream DockTest: an I-shaped shuttle and a T-shaped stop that fit together.
            var shuttleGrid = mapManager.CreateGridEntity(sourceMap.MapId);
            shuttle = shuttleGrid.Owner;
            mapSystem.SetTiles(shuttle, shuttleGrid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(0, 0), new Tile(1)),
                (new Vector2i(0, 1), new Tile(1)),
                (new Vector2i(0, 2), new Tile(1)),
            });
            shuttleDock = entManager.SpawnEntity("AirlockShuttle", new EntityCoordinates(shuttle, new Vector2(0.5f, 0.5f)));

            var targetGrid = mapManager.CreateGridEntity(targetMap.MapId);
            target = targetGrid.Owner;
            mapSystem.SetTiles(target, targetGrid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(0, 0), new Tile(1)),
                (new Vector2i(0, 1), new Tile(1)),
                (new Vector2i(0, 2), new Tile(1)),
                (new Vector2i(-1, 2), new Tile(1)),
                (new Vector2i(1, 2), new Tile(1)),
            });
            targetDock = entManager.SpawnEntity("AirlockShuttle", new EntityCoordinates(target, new Vector2(0.5f, 0.5f)));

            if (withNeighbour)
            {
                // Inside FTLAntiCollisionSystem's 50 m separation radius, clear of the dock.
                var neighbour = mapManager.CreateGridEntity(targetMap.MapId);
                xformSystem.SetLocalPosition(neighbour.Owner, new Vector2(12f, 12f));
                mapSystem.SetTile(neighbour.Owner, neighbour.Comp, Vector2i.Zero, new Tile(1));
            }

            shuttleSystem.FTLToDock(shuttle, entManager.GetComponent<ShuttleComponent>(shuttle), target,
                startupTime: StartupTime, hyperspaceTime: shuttleSystem.DefaultArrivalTime + TravelSeconds);

            // The jump spools up where the shuttle is; it doesn't teleport to the dock straight away.
            Assert.That(entManager.GetComponent<TransformComponent>(shuttle).MapID, Is.EqualTo(sourceMap.MapId),
                "FTLToDock shouldn't move the shuttle before the jump.");
            Assert.That(entManager.GetComponent<DockingComponent>(shuttleDock).Docked, Is.False,
                "FTLToDock shouldn't dock the shuttle before the jump.");
        });

        var states = new List<FTLState>();
        var arrived = false;
        for (var tick = 0; tick < MaxSeconds * 60 && !arrived; tick++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() =>
            {
                if (!entManager.TryGetComponent(shuttle, out FTLComponent ftl))
                {
                    arrived = true;
                    return;
                }

                if (states.Count == 0 || states[^1] != ftl.State)
                    states.Add(ftl.State);

                arrived = ftl.State == FTLState.Cooldown;
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(states, Is.EqualTo(new[]
            {
                FTLState.Starting,
                FTLState.Travelling,
                FTLState.Arriving,
                FTLState.Cooldown,
            }), "The shuttle should go through every FTL state in order.");

            var dock = entManager.GetComponent<DockingComponent>(shuttleDock);
            Assert.That(entManager.GetComponent<TransformComponent>(shuttle).MapID, Is.EqualTo(targetMap.MapId),
                "The shuttle should arrive on the stop's map.");
            Assert.That(dock.DockedWith, Is.EqualTo(targetDock), "The shuttle should arrive docked to the stop.");

            var gap = xformSystem.GetWorldPosition(shuttleDock) - xformSystem.GetWorldPosition(targetDock);
            Assert.That(gap.Length(), Is.LessThan(2f), "The shuttle should still sit at the stop's dock.");
            Assert.That(xformSystem.GetWorldPosition(target).Length(), Is.LessThan(0.5f),
                "The arriving shuttle shouldn't drag the stop out of place.");

            // The FTL map outlives the jump; drop it with the test maps so the pooled server stays clean.
            var maps = new List<EntityUid>();
            var query = entManager.AllEntityQueryEnumerator<FTLMapComponent>();
            while (query.MoveNext(out var ftlMap, out _))
            {
                maps.Add(ftlMap);
            }

            maps.Add(sourceMap.MapUid);
            maps.Add(targetMap.MapUid);
            foreach (var map in maps)
            {
                entManager.DeleteEntity(map);
            }
        });

        await pair.CleanReturnAsync();
    }
}
