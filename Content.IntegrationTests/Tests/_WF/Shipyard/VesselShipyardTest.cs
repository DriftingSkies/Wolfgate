using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.RoundSetup;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.Tether.Harpoon;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Maps;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Shipyard;

/// <summary>Checks every vessel grid, loaded into an initialised map as the shipyard spawns it.</summary>
// The name ends in ShipyardTest so the shipyard CI workflow's filter runs it when a grid changes.
[TestFixture]
public sealed class VesselShipyardTest
{
    private const int CrewStartPicks = 10;

    /// <summary>Every vessel loads as a shuttle, every hardpoint-only gun on it mounts on a hardpoint, and the crew planner posts the pilot by a square helm, and crew starts land inside the hull.</summary>
    [Test]
    public async Task VesselsLoadAsShuttlesWithMountedGuns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var loader = entities.System<MapLoaderSystem>();
        var maps = entities.System<SharedMapSystem>();
        var planner = entities.System<WFCrewPlannerSystem>();
        var safeSpawn = entities.System<WFSafeSpawnSystem>();
        var atmos = entities.System<AtmosphereSystem>();
        var turf = entities.System<TurfSystem>();
        var crewStarts = 0;
        var failures = new List<string>();
        var guns = 0;
        var helmShips = 0;

        await server.WaitPost(() =>
        {
            foreach (var vessel in prototypes.EnumeratePrototypes<VesselPrototype>())
            {
                maps.CreateMap(out var mapId);
                if (!loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var grid))
                {
                    failures.Add($"{vessel.ID}: {vessel.ShuttlePath} did not load.");
                    maps.DeleteMap(mapId);
                    continue;
                }

                if (!entities.HasComponent<ShuttleComponent>(grid.Value))
                    failures.Add($"{vessel.ID}: the grid has no Shuttle component.");

                // Fire control refuses a gun that isn't mounted on a hardpoint.
                var query = entities.EntityQueryEnumerator<HardpointAnchorableOnlyComponent, TransformComponent>();
                while (query.MoveNext(out var gun, out var mount, out var xform))
                {
                    if (xform.GridUid != grid.Value.Owner)
                        continue;
                    guns++;
                    if (mount.anchoredTo == null)
                        failures.Add($"{vessel.ID}: {entities.ToPrettyString(gun)} at {xform.LocalPosition} has no compatible hardpoint on its tile.");
                }

                // The pilot is posted beside a console square to the hull when the grid has one with room beside it.
                var squares = new List<Vector2>();
                var others = new List<Vector2>();
                var consoles = entities.EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
                while (consoles.MoveNext(out _, out _, out var consoleXform))
                {
                    if (consoleXform.GridUid != grid.Value.Owner || !consoleXform.Anchored)
                        continue;

                    (WFPilotDutySystem.IsSquareHelm(consoleXform.LocalRotation) ? squares : others).Add(consoleXform.LocalPosition);
                }

                var posts = planner.Plan(grid.Value.Owner);
                // A hull whose consoles are all boxed in has no helm post; its pilot walks from the deck.
                if (squares.Count > 0 && !posts.Any(post => post.Kind == WFCrewPostKind.Marker)
                    && posts.FirstOrDefault(post => post.Kind == WFCrewPostKind.Helm) is { Kind: WFCrewPostKind.Helm } pilot)
                {
                    helmShips++;
                    if (!squares.Any(console => Vector2.Distance(console, pilot.Coordinates.Position) <= 1.1f)
                        && others.Any(console => Vector2.Distance(console, pilot.Coordinates.Position) <= 1.1f))
                        failures.Add($"{vessel.ID}: the pilot is posted beside a turned console, not the square one.");
                }

                // Crew starts never land outside the hull or on a gun. Drones and hull attachments have no console and take no crew.
                if (squares.Count + others.Count > 0)
                {
                    crewStarts++;
                    var consoleTiles = squares.Concat(others)
                        .Select(pos => maps.TileIndicesFor(grid.Value, new Robust.Shared.Map.EntityCoordinates(grid.Value, pos)))
                        .ToList();
                    for (var i = 0; i < CrewStartPicks; i++)
                    {
                        if (!safeSpawn.TryFindSafeFloor(grid.Value, out var start))
                        {
                            failures.Add($"{vessel.ID}: no safe crew start.");
                            break;
                        }
                        var index = maps.TileIndicesFor(grid.Value, start);
                        if (CrewStartProblem(entities, maps, atmos, turf, grid.Value, index, consoleTiles) is { } problem)
                        {
                            failures.Add($"{vessel.ID}: crew start {index} {problem}.");
                            break;
                        }
                    }
                }

                maps.DeleteMap(mapId);
            }
        });

        Assert.That(guns, Is.Positive, "No hardpoint-only guns were found on any vessel.");
        Assert.That(helmShips, Is.Positive, "No vessel with a square console was planned.");
        Assert.That(crewStarts, Is.Positive, "No vessel took a crew start.");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        await pair.CleanReturnAsync();
    }

    /// <summary>Every seat on every vessel keeps whoever buckles into it, so none is jammed against a table or wall.</summary>
    [Test]
    public async Task VesselSeatsHoldOccupants()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var loader = entities.System<MapLoaderSystem>();
        var maps = entities.System<SharedMapSystem>();
        var buckle = entities.System<SharedBuckleSystem>();
        var containers = entities.System<SharedContainerSystem>();
        var failures = new List<string>();
        var seats = 0;

        foreach (var vessel in prototypes.EnumeratePrototypes<VesselPrototype>())
        {
            var mapId = MapId.Nullspace;
            var seated = new List<(EntityUid Mob, EntityUid Seat, Vector2 Position)>();
            await server.WaitPost(() =>
            {
                maps.CreateMap(out mapId);
                if (!loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var grid))
                {
                    failures.Add($"{vessel.ID}: {vessel.ShuttlePath} did not load.");
                    return;
                }

                var straps = new List<EntityUid>();
                var query = entities.EntityQueryEnumerator<StrapComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var strap, out var xform))
                {
                    // Folded and stored seats refuse a buckle by design. A loose chair (an office chair) can be shoved
                    // off by the mob seated next to it, so only fixed seats are checked.
                    if (xform.GridUid == grid.Value.Owner && strap.Enabled && xform.Anchored && !containers.IsEntityInContainer(uid) &&
                        !entities.HasComponent<ShipHarpoonTurretComponent>(uid))
                        straps.Add(uid);
                }

                foreach (var seat in straps)
                {
                    var xform = entities.GetComponent<TransformComponent>(seat);
                    var mob = entities.SpawnEntity("MobHuman", xform.Coordinates);
                    if (buckle.TryBuckle(mob, mob, seat))
                        seated.Add((mob, seat, xform.LocalPosition));
                    else
                        failures.Add($"{vessel.ID}: {entities.ToPrettyString(seat)} at {xform.LocalPosition} can't be buckled into.");
                }
            });

            await pair.RunTicksSync(30);

            await server.WaitPost(() =>
            {
                foreach (var (mob, seat, position) in seated)
                {
                    seats++;
                    if (entities.GetComponent<BuckleComponent>(mob).BuckledTo != seat)
                        failures.Add($"{vessel.ID}: {entities.ToPrettyString(seat)} at {position} drops whoever buckles in.");
                }

                maps.DeleteMap(mapId);
            });
        }

        Assert.That(seats, Is.Positive, "No vessel seat was buckled into.");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        await pair.CleanReturnAsync();
    }

    /// <summary>Describes why a crew start tile is unsafe, or null: it must hold no gun and be breathable, enclosed or in the cockpit.</summary>
    private static string CrewStartProblem(IEntityManager entities, SharedMapSystem maps, AtmosphereSystem atmos,
        TurfSystem turf, Entity<MapGridComponent> grid, Vector2i index, List<Vector2i> consoles)
    {
        var anchored = maps.GetAnchoredEntities(grid, grid.Comp, index);
        while (anchored.MoveNext(out var ent))
        {
            if (entities.HasComponent<GunComponent>(ent.Value))
                return $"is under {entities.ToPrettyString(ent.Value)}";
        }

        var map = entities.GetComponent<TransformComponent>(grid).MapUid;
        if (!atmos.IsTileSpace(grid.Owner, map, index) && atmos.GetTileMixture(grid.Owner, map, index) is { Pressure: >= 80f and <= 160f })
            return null;
        var enclosed = true;
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (!maps.TryGetTileRef(grid, grid.Comp, index + new Vector2i(x, y), out var tile) || tile.Tile.IsEmpty || turf.IsSpace(tile))
                    enclosed = false;
            }
        }
        if (enclosed || consoles.Any(c => Math.Abs(c.X - index.X) <= 1 && Math.Abs(c.Y - index.Y) <= 1))
            return null;
        return "is exterior";
    }
}
