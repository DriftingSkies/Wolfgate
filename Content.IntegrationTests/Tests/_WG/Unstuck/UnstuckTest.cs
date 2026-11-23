#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.Drydock;
using Content.Server._WG.Unstuck;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared.Cuffs;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Unstuck;

/// <summary>
/// Unstick me moves a body out of a wall to the nearest clear floor, does nothing for one that isn't stuck, and puts a
/// vehicle's occupant back outside.
/// </summary>
[TestFixture]
[TestOf(typeof(WGUnstuckSystem))]
public sealed class UnstuckTest
{
    private const string MobProto = "MobHuman";
    private const string WallProto = "WallSolid";
    private const string ArmorProto = "WallArmorAddonSolid";

    /// <summary>The interior test chassis from <c>VehicleInteriorTest</c>; its doorways are a metre to either side.</summary>
    private const string VehicleProto = "WGInteriorTestVehicle";

    private static readonly Vector2i WallTile = new(2, 2);
    private static readonly Vector2 VehicleSpot = new(5.5f, 5.5f);

    /// <summary>A living occupant of an unlocked vehicle is put outside it.</summary>
    [Test]
    public async Task LivingLeaveAnUnlockedVehicle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var (vehicle, mob) = await Board(pair);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<WGUnstuckSystem>().TryUnstuck(mob, out var message), Is.True, message);
            Assert.That(em.GetComponent<TransformComponent>(mob).MapID, Is.EqualTo(em.GetComponent<TransformComponent>(vehicle).MapID),
                "The body isn't back outside by the vehicle.");
            Assert.That(em.HasComponent<VehicleInteriorOccupantComponent>(mob), Is.False, "The body still counts as inside.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A locked vehicle keeps a living occupant in, but not a dead one.</summary>
    [Test]
    public async Task LockedVehicleHoldsTheLivingButNotTheDead()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var (vehicle, mob) = await Board(pair);

        await server.WaitAssertion(() =>
        {
#pragma warning disable RA0002 // The test locks the vehicle directly.
            em.GetComponent<VehicleLockComponent>(vehicle).Locked = true;
#pragma warning restore RA0002

            var unstuck = server.System<WGUnstuckSystem>();
            Assert.That(unstuck.TryUnstuck(mob, out _), Is.False, "A living body got past the lock.");

            server.System<MobStateSystem>().ChangeMobState(mob, MobState.Dead);
            Assert.That(unstuck.TryUnstuck(mob, out var message), Is.True, message);
            Assert.That(em.GetComponent<TransformComponent>(mob).MapID, Is.EqualTo(em.GetComponent<TransformComponent>(vehicle).MapID),
                "The dead body isn't back outside by the vehicle.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A cuffed occupant of an unlocked vehicle stays in, as the door wouldn't let them out either.</summary>
    [Test]
    public async Task CuffedStayInAVehicle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var (_, mob) = await Board(pair);

        await server.WaitAssertion(() =>
        {
            var cuffs = em.SpawnEntity("Handcuffs", em.GetComponent<TransformComponent>(mob).Coordinates);
            Assert.That(server.System<SharedCuffableSystem>().TryAddNewCuffs(mob, mob, cuffs), Is.True, "The cuffs didn't go on.");
            Assert.That(server.System<WGUnstuckSystem>().TryUnstuck(mob, out _), Is.False, "A cuffed body slipped out of the vehicle.");
            Assert.That(em.HasComponent<VehicleInteriorOccupantComponent>(mob), Is.True, "A cuffed body left the vehicle.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A knocked-down occupant stays in, though being down doesn't block interaction.</summary>
    [Test]
    public async Task KnockedDownStayInAVehicle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var (_, mob) = await Board(pair);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedStunSystem>().TryKnockdown(mob, TimeSpan.FromSeconds(30), true), Is.True,
                "The body wasn't knocked down.");
            Assert.That(server.System<WGUnstuckSystem>().TryUnstuck(mob, out _), Is.False, "A knocked-down body slipped out of the vehicle.");
            Assert.That(em.HasComponent<VehicleInteriorOccupantComponent>(mob), Is.True, "A knocked-down body left the vehicle.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The test chassis on a block of plating, with a human who has climbed in through a doorway.</summary>
    private static async Task<(EntityUid Vehicle, EntityUid Mob)> Board(TestPair pair)
    {
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        EntityUid vehicle = default, mob = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -4; x < 16; x++)
            for (var y = -4; y < 16; y++)
                maps.SetTile(grid, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            vehicle = em.SpawnEntity(VehicleProto, new EntityCoordinates(grid, VehicleSpot));
            mob = em.SpawnEntity(MobProto, new EntityCoordinates(grid, VehicleSpot + new Vector2(1f, 0f)));
        });
        await server.WaitRunTicks(5);

        await server.WaitPost(() => server.System<SharedInteractionSystem>().InteractionActivate(mob, vehicle));
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
            Assert.That(em.HasComponent<VehicleInteriorOccupantComponent>(mob), Is.True, "The body never got in."));

        return (vehicle, mob);
    }

    /// <summary>A body inside a wall, nearer its top edge, lands on the floor just above it.</summary>
    [Test]
    public async Task StuckInWallMovesToNearestFloor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            DrydockTest.SetFloor(pair, grid, 5);
            DrydockTest.SpawnAnchored(pair, grid, WallProto, WallTile);
            var mob = em.SpawnEntity(MobProto, new EntityCoordinates(grid, WallTile.X + 0.5f, WallTile.Y + 0.8f));

            var unstuck = server.System<WGUnstuckSystem>();
            Assert.That(unstuck.TryUnstuck(mob, out var message), Is.True, message);

            var xform = em.GetComponent<TransformComponent>(mob);
            var tile = server.System<SharedMapSystem>()
                .TileIndicesFor(grid, em.GetComponent<MapGridComponent>(grid), xform.Coordinates);
            Assert.That(tile, Is.EqualTo(WallTile + new Vector2i(0, 1)), "The body didn't land on the nearest floor.");

            Assert.That(unstuck.TryUnstuck(mob, out message), Is.False, "A freed body still counted as stuck.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A body mostly on the airless side of a wall line comes out on that side, not through the wall into the air.
    /// </summary>
    [Test]
    public async Task AirDoesNotPullThroughAWall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitPost(() =>
        {
            DrydockTest.SetFloor(pair, grid, 5);
            for (var x = 0; x < 5; x++)
                DrydockTest.SpawnAnchored(pair, grid, WallProto, new Vector2i(x, WallTile.Y));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var atmos = server.System<AtmosphereSystem>();
            for (var x = 0; x < 5; x++)
            for (var y = WallTile.Y + 1; y < 5; y++)
            {
                var mix = atmos.GetTileMixture(grid, map.MapUid, new Vector2i(x, y), true);
                Assert.That(mix, Is.Not.Null, "The floor above the wall has no air to fill.");
                mix!.AdjustMoles(Gas.Oxygen, Atmospherics.MolesCellStandard * 0.21f);
                mix.AdjustMoles(Gas.Nitrogen, Atmospherics.MolesCellStandard * 0.79f);
                mix.Temperature = Atmospherics.T20C;
            }

            Assert.That(atmos.IsTileMixtureProbablySafe(grid, map.MapUid, WallTile + new Vector2i(0, 1)), Is.True, "The air above isn't breathable.");
            Assert.That(atmos.IsTileMixtureProbablySafe(grid, map.MapUid, WallTile - new Vector2i(0, 1)), Is.False, "The floor below has air.");

            var mob = em.SpawnEntity(MobProto, new EntityCoordinates(grid, WallTile.X + 0.5f, WallTile.Y + 0.2f));
            Assert.That(server.System<WGUnstuckSystem>().TryUnstuck(mob, out var message), Is.True, message);
            var tile = server.System<SharedMapSystem>()
                .TileIndicesFor(grid, em.GetComponent<MapGridComponent>(grid), em.GetComponent<TransformComponent>(mob).Coordinates);
            Assert.That(tile, Is.EqualTo(WallTile - new Vector2i(0, 1)), "The body was pulled through the wall to the air.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A body in a wall two tiles thick, as near the air beyond it as the airless floor below, comes out below: the air
    /// would win the tie, but the second wall is in the way.
    /// </summary>
    [Test]
    public async Task AirDoesNotPullThroughASecondWall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var airRow = WallTile.Y + 2;

        await server.WaitPost(() =>
        {
            DrydockTest.SetFloor(pair, grid, 5);
            for (var x = 0; x < 5; x++)
            {
                DrydockTest.SpawnAnchored(pair, grid, WallProto, new Vector2i(x, WallTile.Y));
                DrydockTest.SpawnAnchored(pair, grid, WallProto, new Vector2i(x, WallTile.Y + 1));
            }
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var atmos = server.System<AtmosphereSystem>();
            for (var x = 0; x < 5; x++)
            {
                var mix = atmos.GetTileMixture(grid, map.MapUid, new Vector2i(x, airRow), true);
                Assert.That(mix, Is.Not.Null, "The floor beyond the walls has no air to fill.");
                mix!.AdjustMoles(Gas.Oxygen, Atmospherics.MolesCellStandard * 0.21f);
                mix.AdjustMoles(Gas.Nitrogen, Atmospherics.MolesCellStandard * 0.79f);
                mix.Temperature = Atmospherics.T20C;
            }

            Assert.That(atmos.IsTileMixtureProbablySafe(grid, map.MapUid, new Vector2i(WallTile.X, airRow)), Is.True, "The air beyond isn't breathable.");
            Assert.That(atmos.IsTileMixtureProbablySafe(grid, map.MapUid, WallTile - new Vector2i(0, 1)), Is.False, "The floor below has air.");

            // 1.49 tiles from the floor below and 1.51 from the air, inside the tie.
            var mob = em.SpawnEntity(MobProto, new EntityCoordinates(grid, WallTile.X + 0.5f, WallTile.Y + 0.99f));
            Assert.That(server.System<WGUnstuckSystem>().TryUnstuck(mob, out var message), Is.True, message);
            var tile = server.System<SharedMapSystem>()
                .TileIndicesFor(grid, em.GetComponent<MapGridComponent>(grid), em.GetComponent<TransformComponent>(mob).Coordinates);
            Assert.That(tile, Is.EqualTo(WallTile - new Vector2i(0, 1)), "The body was moved through the second wall to the air.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A body in the corner of an L of walls doesn't come out through the corner onto the diagonal floor beyond it, though
    /// that floor is nearest: the way there clips the wall beside the corner.
    /// </summary>
    [Test]
    public async Task DoesNotSlipPastACorner()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            DrydockTest.SetFloor(pair, grid, 5);
            DrydockTest.SpawnAnchored(pair, grid, WallProto, WallTile);
            DrydockTest.SpawnAnchored(pair, grid, WallProto, WallTile + new Vector2i(1, 0));
            DrydockTest.SpawnAnchored(pair, grid, WallProto, WallTile + new Vector2i(0, 1));

            // 0.81 tiles from the diagonal floor's centre, whose path clips the wall to the right; 1.47 from the floor below.
            var mob = em.SpawnEntity(MobProto, new EntityCoordinates(grid, WallTile.X + 0.95f, WallTile.Y + 0.9f));
            Assert.That(server.System<WGUnstuckSystem>().TryUnstuck(mob, out var message), Is.True, message);
            var tile = server.System<SharedMapSystem>()
                .TileIndicesFor(grid, em.GetComponent<MapGridComponent>(grid), em.GetComponent<TransformComponent>(mob).Coordinates);
            Assert.That(tile, Is.Not.EqualTo(WallTile + new Vector2i(1, 1)), "The body slipped past the corner.");
            Assert.That(tile, Is.EqualTo(WallTile - new Vector2i(0, 1)), "The body didn't land on the nearest floor it could reach.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A body out in space, wedged into the armor bolted to a hull, comes out in open space beside it: no floor is in
    /// reach without passing through the hull, and the tile it is over is still overhung by the armor.
    /// </summary>
    [Test]
    public async Task OutsideAHullComesOutInSpace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        const int hullRow = 4;

        await server.WaitAssertion(() =>
        {
            var xforms = server.System<SharedTransformSystem>();
            DrydockTest.SetFloor(pair, grid, 5);
            for (var x = 0; x < 5; x++)
                DrydockTest.SpawnAnchored(pair, grid, WallProto, new Vector2i(x, hullRow));

            // The armor sits on the hull's wall and reaches 0.3 tiles out over the space beyond it.
            DrydockTest.SpawnAnchored(pair, grid, ArmorProto, new Vector2i(2, hullRow));

            var spot = xforms.ToMapCoordinates(new EntityCoordinates(grid, 2.5f, hullRow + 1.45f));
            var mob = em.SpawnEntity(MobProto, spot);
            Assert.That(em.GetComponent<TransformComponent>(mob).GridUid, Is.Null, "The body isn't out in space.");

            var unstuck = server.System<WGUnstuckSystem>();
            Assert.That(unstuck.TryUnstuck(mob, out var message), Is.True, message);

            var local = Vector2.Transform(xforms.GetWorldPosition(mob), xforms.GetInvWorldMatrix(grid));
            Assert.That(local.Y, Is.GreaterThan(hullRow + 1f), "The body was moved through the hull.");
            Assert.That(unstuck.TryUnstuck(mob, out message), Is.False, $"The body is still stuck at {local}: {message}");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A body on open floor, or standing against a wall, is left where it is.</summary>
    [Test]
    public async Task FreeBodyIsNotMoved()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            DrydockTest.SetFloor(pair, grid, 5);
            DrydockTest.SpawnAnchored(pair, grid, WallProto, WallTile);
            var unstuck = server.System<WGUnstuckSystem>();

            foreach (var spot in new[] { new Vector2(0.5f, 0.5f), new Vector2(WallTile.X + 0.5f, WallTile.Y + 1.4f) })
            {
                var start = new EntityCoordinates(grid, spot);
                var mob = em.SpawnEntity(MobProto, start);
                Assert.That(unstuck.TryUnstuck(mob, out _), Is.False, $"A body at {spot} counted as stuck.");
                Assert.That(em.GetComponent<TransformComponent>(mob).Coordinates, Is.EqualTo(start), $"A body at {spot} was moved.");
                em.DeleteEntity(mob);
            }
        });

        await pair.CleanReturnAsync();
    }
}
