#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._NF.Shuttles.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.NPC;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>
/// Spike F0.1: can a smashing NPC on a planet's ground get into a separate, anchored outpost grid walled all round?
/// The ground and the outpost are two navmeshes, and a path crosses between grids only through a pathfinding portal,
/// as docked ships do.
/// </summary>
[TestFixture]
[TestOf(typeof(PathfindingSystem))]
public sealed class OutpostBreachSpikeTest
{
    /// <summary>Side of the outpost in tiles; its outer ring is all wall.</summary>
    private const int Side = 5;

    /// <summary>Where the NPC starts, 20 tiles west of the outpost's wall.</summary>
    private static readonly Vector2 Start = new(-20.5f, 0.5f);

    /// <summary>The outpost's local origin on the ground, so its tiles cover x 0..4, y -2..2.</summary>
    private static readonly Vector2 Origin = new(0f, -2f);

    private static int EdgeCount => 4 * (Side - 1);

    /// <summary>The ground, and the grid the walled outpost stands on: its own grid, or the ground itself in the control.</summary>
    private sealed record World(EntityUid Ground, EntityUid Outpost, Vector2 Origin);

    private sealed record Run(bool Inside, SteeringStatus Status, int WallsLeft, float Seconds, string Trace);

    /// <summary>Without a portal the ground's navmesh never reaches the outpost: the NPC finds no path and breaks nothing.</summary>
    [Test]
    public async Task WithoutPortalsTheGroundNeverReachesTheOutpost()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var world = await Build(pair, portals: false);
        var run = await Send(pair, world, breach: false);
        TestContext.Out.WriteLine($"no portals: {run}");

        Assert.Multiple(() =>
        {
            Assert.That(run.Status, Is.EqualTo(SteeringStatus.NoPath), $"The NPC found a way onto a grid with no portal: {run.Trace}.");
            Assert.That(run.Inside, Is.False, "The NPC got in without a portal.");
            Assert.That(run.WallsLeft, Is.EqualTo(EdgeCount), "The NPC broke a wall it had no path to.");
        });

        await Teardown(pair, world);
    }

    /// <summary>
    /// With portals but sent straight at the middle from 20 tiles out, the smashing path is never found: each wall
    /// costs 10 + its HP / 100 tiles' worth, so A* spends its 512 nodes on open ground first.
    /// </summary>
    [Test]
    public async Task WithPortalsAFarSmashingPathRunsOutOfNodes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var world = await Build(pair, portals: true);
        var run = await Send(pair, world, breach: false);
        TestContext.Out.WriteLine($"portals, direct: {run}");

        Assert.That(run.Status, Is.EqualTo(SteeringStatus.NoPath),
            $"A* found the far smashing path; the breach operator may no longer need its walk-up stage: {run.Trace}.");

        await Teardown(pair, world);
    }

    /// <summary>
    /// The breach operator's shape: walk the ground to just outside the nearest edge tile, melee whatever blocks that
    /// tile as a target of its own, then walk in. The NPC breaks one wall and stands on the outpost.
    /// </summary>
    [Test]
    public async Task WithPortalsTheBreachWalkUpGetsIn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var world = await Build(pair, portals: true);
        var run = await Send(pair, world, breach: true);
        TestContext.Out.WriteLine($"portals, breach: {run}");

        Assert.Multiple(() =>
        {
            Assert.That(run.Status, Is.Not.EqualTo(SteeringStatus.NoPath), $"The NPC found no path: {run.Trace}.");
            Assert.That(run.WallsLeft, Is.LessThan(EdgeCount), "The NPC broke no wall.");
            Assert.That(run.Inside, Is.True, $"The NPC never stood inside the outpost: {run.Trace}.");
        });

        await Teardown(pair, world);
    }

    /// <summary>
    /// Without portals the breach breaks the wall but never gets in: the NPC can attack a wall within reach, but no path
    /// leads from the ground onto the other grid, so it stands outside the hole it made.
    /// </summary>
    [Test]
    public async Task WithoutPortalsTheBreachNeverGetsIn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var world = await Build(pair, portals: false);
        var run = await Send(pair, world, breach: true);
        TestContext.Out.WriteLine($"no portals, breach: {run}");

        Assert.Multiple(() =>
        {
            Assert.That(run.Inside, Is.False, $"The NPC got in without a portal: {run.Trace}.");
            Assert.That(run.Status, Is.EqualTo(SteeringStatus.NoPath), $"The NPC found a way onto a grid with no portal: {run.Trace}.");
        });

        await Teardown(pair, world);
    }

    /// <summary>Control: the same walls built straight onto the ground, with the same walk-up, are broken through.</summary>
    [Test]
    public async Task ControlOnOneGridTheBreachWalkUpGetsIn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var world = await Build(pair, portals: false, separateGrid: false);
        var run = await Send(pair, world, breach: true);
        TestContext.Out.WriteLine($"control, breach: {run}");

        Assert.Multiple(() =>
        {
            Assert.That(run.WallsLeft, Is.LessThan(EdgeCount), $"The NPC broke no wall: {run.Trace}.");
            Assert.That(run.Inside, Is.True, $"The NPC never stood inside: {run.Trace}.");
        });

        await Teardown(pair, world);
    }

    /// <summary>
    /// A planet-like ground map with a hole carved where the outpost sits, and the outpost as its own grid in it,
    /// anchored as a founded outpost will be. With <paramref name="portals"/> every edge tile of the outpost is joined
    /// to the ground tile outside it.
    /// </summary>
    private static async Task<World> Build(Pair.TestPair pair, bool portals, bool separateGrid = true)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        World world = default!;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.NPCPauseWhenNoPlayersInRange, false);
            var maps = server.System<SharedMapSystem>();
            var xforms = server.System<SharedTransformSystem>();
            var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);

            var ground = maps.CreateMap(out var mapId);
            var groundGrid = entMan.EnsureComponent<MapGridComponent>(ground);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -30; x <= 12; x++)
            for (var y = -10; y <= 10; y++)
            {
                if (separateGrid && x >= Origin.X && x < Origin.X + Side && y >= Origin.Y && y < Origin.Y + Side)
                    continue;
                tiles.Add((new Vector2i(x, y), plating));
            }

            maps.SetTiles(ground, groundGrid, tiles);
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(ground, false, air);
            var gravity = entMan.EnsureComponent<GravityComponent>(ground);
            gravity.Enabled = true;
            gravity.Inherent = true;
            entMan.Dirty(ground, gravity);
            entMan.EnsureComponent<WFPlanetLayerComponent>(ground);

            // The control builds the same walls straight onto the ground.
            var outpost = ground;
            var origin = Origin;
            if (separateGrid)
            {
                var grid = maps.CreateGridEntity(mapId);
                xforms.SetWorldPosition(grid.Owner, Origin);
                var floor = new List<(Vector2i, Tile)>();
                for (var x = 0; x < Side; x++)
                for (var y = 0; y < Side; y++)
                    floor.Add((new Vector2i(x, y), plating));
                maps.SetTiles(grid.Owner, grid.Comp, floor);
                entMan.EnsureComponent<ForceAnchorComponent>(grid);
                entMan.EnsureComponent<PreventGridAnchorChangesComponent>(grid);
                server.System<ShuttleSystem>().Disable(grid);
                outpost = grid.Owner;
                origin = Vector2.Zero;
            }

            for (var x = 0; x < Side; x++)
            for (var y = 0; y < Side; y++)
            {
                if (!IsEdge(x, y))
                    continue;
                entMan.SpawnEntity("WallSolid", new EntityCoordinates(outpost, origin.X + x + 0.5f, origin.Y + y + 0.5f));
                if (!portals)
                    continue;

                var outside = Outside(x, y) + Origin;
                Assert.That(server.System<PathfindingSystem>().TryCreatePortal(
                        new EntityCoordinates(ground, outside.X + 0.5f, outside.Y + 0.5f),
                        new EntityCoordinates(outpost, x + 0.5f, y + 0.5f),
                        out _),
                    Is.True, "The portal between the ground and the outpost was refused.");
            }

            world = new World(ground, outpost, origin);
        });

        await server.WaitRunTicks(10);
        return world;
    }

    private static bool IsEdge(int x, int y) => x == 0 || y == 0 || x == Side - 1 || y == Side - 1;

    /// <summary>The outpost-local tile just outside an edge tile, pushed out along whichever side it is on.</summary>
    private static Vector2 Outside(int x, int y)
    {
        if (x == 0)
            return new Vector2(-1, y);
        if (x == Side - 1)
            return new Vector2(Side, y);
        return y == 0 ? new Vector2(x, -1) : new Vector2(x, Side);
    }

    /// <summary>
    /// Sends a smashing xeno from 20 tiles out to the middle of the outpost and watches it for up to two minutes. With
    /// <paramref name="breach"/> it goes in the breach operator's three stages: walk up, attack, enter.
    /// </summary>
    private static async Task<Run> Send(Pair.TestPair pair, World world, bool breach)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var steeringSystem = server.System<NPCSteeringSystem>();
        var centre = new EntityCoordinates(world.Outpost, world.Origin.X + Side / 2f, world.Origin.Y + Side / 2f);
        var centreWorld = Origin + new Vector2(Side / 2f);
        EntityUid npc = default;
        var walkUp = Vector2.Zero;
        var breachTile = Vector2i.Zero;
        await server.WaitPost(() =>
        {
            npc = entMan.SpawnEntity("MobXeno", new EntityCoordinates(world.Ground, Start.X, Start.Y));
            // The xeno keeps its blackboard (NavSmash, NavPry) but plans nothing of its own.
            server.System<HTNSystem>().SetHTNEnabled((npc, entMan.GetComponent<HTNComponent>(npc)), false);
            entMan.EnsureComponent<ActiveNPCComponent>(npc);
            // How fast a wall falls is the prefab walls' and the raid tiers' balance, not this spike's; 1,300 HP of
            // solid wall at a stock bite is a minute of chewing.
            var blunt = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Blunt");
            entMan.GetComponent<MeleeWeaponComponent>(npc).Damage = new DamageSpecifier(blunt, FixedPoint2.New(150));

            if (!breach)
            {
                steeringSystem.Register(npc, centre);
                return;
            }

            var best = float.MaxValue;
            for (var x = 0; x < Side; x++)
            for (var y = 0; y < Side; y++)
            {
                if (!IsEdge(x, y))
                    continue;
                var outside = Outside(x, y) + Origin + new Vector2(0.5f);
                var distance = Vector2.Distance(outside, Start);
                if (distance >= best)
                    continue;
                best = distance;
                walkUp = outside;
                breachTile = new Vector2i(x, y) + (Vector2i) world.Origin;
            }

            steeringSystem.Register(npc, new EntityCoordinates(world.Ground, walkUp.X, walkUp.Y));
        });

        var inside = false;
        var stage = breach ? BreachStage.WalkUp : BreachStage.Enter;
        var status = SteeringStatus.Moving;
        var trace = string.Empty;
        var ticks = 0;
        var maps = server.System<SharedMapSystem>();
        var stalled = 0;
        for (var i = 0; i < 240 && !inside; i++)
        {
            await server.WaitRunTicks(15);
            ticks += 15;
            await server.WaitPost(() =>
            {
                var xform = entMan.GetComponent<TransformComponent>(npc);
                var at = xforms.GetWorldPosition(xform);
                if (stage == BreachStage.WalkUp && Vector2.Distance(at, walkUp) < 1f)
                    stage = BreachStage.Attack;

                // The attack stage: melee whatever still blocks the breach tile (the wall, then its girder), as a
                // target of its own rather than through steering's obstacle handling.
                if (stage == BreachStage.Attack)
                {
                    if (Blocker(entMan, maps, world.Outpost, breachTile) is { } blocker)
                    {
                        entMan.EnsureComponent<NPCMeleeCombatComponent>(npc).Target = blocker;
                        // Steering's own smash gives up between hits and leaves no path, which makes melee call the
                        // target unreachable; start it afresh, as a replanned HTN operator would.
                        if (entMan.TryGetComponent<NPCSteeringComponent>(npc, out var stuck) && stuck.Status == SteeringStatus.NoPath)
                            steeringSystem.Unregister(npc);
                    }
                    else
                    {
                        entMan.RemoveComponent<NPCMeleeCombatComponent>(npc);
                        steeringSystem.Register(npc, centre);
                        stage = BreachStage.Enter;
                    }
                }

                inside = xform.GridUid == world.Outpost && Vector2.Distance(at, centreWorld) < 1.5f;
                if (entMan.TryGetComponent<NPCSteeringComponent>(npc, out var steering))
                {
                    status = steering.Status;
                    var next = steering.CurrentPath.TryPeek(out var node)
                        ? $"{(node.GraphUid == world.Outpost ? "outpost" : "ground")} {node.Box} dmg {node.Data.Damage}"
                        : "none";
                    var wallDamage = 0f;
                    var damageQuery = entMan.AllEntityQueryEnumerator<DamageableComponent, TransformComponent>();
                    while (damageQuery.MoveNext(out var damaged, out var damageable, out var wallXform))
                    {
                        if (wallXform.GridUid == world.Outpost && damaged != npc)
                            wallDamage += damageable.TotalDamage.Float();
                    }

                    var velocity = entMan.GetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(npc).LinearVelocity;
                    trace = $"at {at}, stage {stage}, failed paths {steering.FailedPathCount}, path {steering.CurrentPath.Count}, "
                        + $"next {next}, velocity {velocity.Length():F2}, wall damage {wallDamage}, "
                        + $"ground {Navmesh(entMan, world.Ground)}, outpost {Navmesh(entMan, world.Outpost)}";
                }
            });
            // During an attack steering drops to no path between hits and recovers; twenty seconds of it is a stall.
            stalled = status == SteeringStatus.NoPath ? stalled + 1 : 0;
            if (stalled > 0 && stage != BreachStage.Attack || stalled >= 40)
                break;
        }

        var walls = 0;
        await server.WaitPost(() =>
        {
            var query = entMan.AllEntityQueryEnumerator<TransformComponent, MetaDataComponent>();
            while (query.MoveNext(out _, out var xform, out var meta))
            {
                if (xform.GridUid == world.Outpost && meta.EntityPrototype?.ID == "WallSolid")
                    walls++;
            }
        });

        return new Run(inside, status, walls, ticks / 30f, trace);
    }

    private enum BreachStage : byte { WalkUp, Attack, Enter }

    /// <summary>A hard, destructible anchored entity on a tile of the outpost, if anything still blocks it.</summary>
    private static EntityUid? Blocker(IEntityManager entMan, SharedMapSystem maps, EntityUid grid, Vector2i tile)
    {
        var gridComp = entMan.GetComponent<MapGridComponent>(grid);
        var anchored = maps.GetAnchoredEntities(grid, gridComp, tile);
        while (anchored.MoveNext(out var found))
        {
            var ent = found.Value;
            if (entMan.HasComponent<Content.Server.Destructible.DestructibleComponent>(ent)
                && entMan.TryGetComponent<Robust.Shared.Physics.Components.PhysicsComponent>(ent, out var body)
                && body.Hard && body.CanCollide)
                return ent;
        }

        return null;
    }

    /// <summary>A grid's navmesh in brief: chunks built and dirty, and portals linked to a poly.</summary>
    private static string Navmesh(IEntityManager entMan, EntityUid grid)
    {
        if (!entMan.TryGetComponent<GridPathfindingComponent>(grid, out var comp))
            return "no navmesh";

        var linked = 0;
        foreach (var chunk in comp.Chunks.Values)
        {
            linked += chunk.PortalPolys.Count;
        }

        return $"built {comp.Chunks.Count}, dirty {comp.DirtyChunks.Count}, portals linked {linked}";
    }

    private static async Task Teardown(Pair.TestPair pair, World world)
    {
        await pair.Server.WaitPost(() => pair.Server.EntMan.DeleteEntity(world.Ground));
        await pair.CleanReturnAsync();
    }
}
