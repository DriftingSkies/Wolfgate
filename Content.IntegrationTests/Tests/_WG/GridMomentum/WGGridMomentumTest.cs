#nullable enable
using System.Numerics;
using Content.Shared._WG.GridMomentum;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WG.GridMomentum;

/// <summary>A mob that floats off a moving grid keeps the grid's momentum.</summary>
[TestFixture]
[TestOf(typeof(SharedMoverController))]
public sealed class WGGridMomentumTest
{
    [Test]
    public async Task MobThatFloatsOffAMovingGridKeepsItsMomentum()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var physics = server.System<SharedPhysicsSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var shipVelocity = new Vector2(6f, 0f);

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            var body = entMan.GetComponent<PhysicsComponent>(grid);
            physics.SetBodyType(grid, BodyType.Dynamic, body: body);
            physics.SetLinearDamping(grid, body, 0f);
            physics.SetAngularDamping(grid, body, 0f);
            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            physics.SetLinearVelocity(grid, shipVelocity, body: body);
        });

        // Aboard for a moment, then off the edge into space, as stepping off would put it.
        await HoldShip(server, physics, grid, shipVelocity, 10);
        await server.WaitPost(() =>
        {
            var origin = xforms.GetWorldPosition(grid);
            xforms.SetMapCoordinates(mob, new MapCoordinates(origin + new Vector2(2.5f, 0.5f), map.MapId));
        });
        await HoldShip(server, physics, grid, shipVelocity, 60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(mob).GridUid, Is.Null, "The mob must float off the grid.");
            var mobVelocity = entMan.GetComponent<PhysicsComponent>(mob).LinearVelocity;
            Assert.That((mobVelocity - shipVelocity).Length(), Is.LessThan(0.1f),
                $"A mob off a moving grid must keep drifting with it, not brake against the map; it moves at {mobVelocity}.");
        });

        // The ship turns back and leaves the mob behind: on its own, the mob stops being driven at the ship's old speed.
        await HoldShip(server, physics, grid, -shipVelocity, 40);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGGridMomentumComponent>(mob).Frame, Is.EqualTo(Vector2.Zero),
                "A mob far from the grid it left still carries that grid's velocity.");
        });

        // It bumps into something that stops it, and nothing drives it back up to speed.
        await server.WaitPost(() => physics.SetLinearVelocity(mob, Vector2.Zero));
        await server.WaitRunTicks(30);
        await server.WaitAssertion(() =>
        {
            var mobVelocity = entMan.GetComponent<PhysicsComponent>(mob).LinearVelocity;
            Assert.That(mobVelocity.Length(), Is.LessThan(0.1f), $"A stopped mob was driven back up to {mobVelocity}.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A ship that sets off past a mob floating in space doesn't drag it along.</summary>
    [Test]
    public async Task PassingShipDoesNotDragAFloatingMob()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var physics = server.System<SharedPhysicsSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var shipVelocity = new Vector2(20f, 0f);

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            var body = entMan.GetComponent<PhysicsComponent>(grid);
            physics.SetBodyType(grid, BodyType.Dynamic, body: body);
            physics.SetLinearDamping(grid, body, 0f);
            physics.SetAngularDamping(grid, body, 0f);
            var origin = xforms.GetWorldPosition(grid);
            mob = entMan.SpawnEntity("MobHuman", new MapCoordinates(origin + new Vector2(0.5f, 2.5f), map.MapId));
        });

        await HoldShip(server, physics, grid, shipVelocity, 30);

        await server.WaitAssertion(() =>
        {
            var mobVelocity = entMan.GetComponent<PhysicsComponent>(mob).LinearVelocity;
            Assert.That(mobVelocity.Length(), Is.LessThan(0.5f), $"The ship dragged the floating mob up to {mobVelocity}.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A deleted grid is dropped from every mob that stood on it or floated off it, so it is never networked.</summary>
    [Test]
    public async Task DeletedGridIsForgotten()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid floater = default;
        EntityUid stander = default;
        await server.WaitPost(() =>
            floater = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f))));
        await server.WaitRunTicks(5);

        // One mob floats off beside the grid and still remembers it as the grid it left; another takes its place.
        await server.WaitPost(() =>
        {
            var origin = xforms.GetWorldPosition(grid);
            xforms.SetMapCoordinates(floater, new MapCoordinates(origin + new Vector2(2.5f, 0.5f), map.MapId));
            stander = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGGridMomentumComponent>(floater).Left, Is.EqualTo(grid),
                "The floating mob must remember the grid it left.");
            Assert.That(entMan.GetComponent<WGGridMomentumComponent>(stander).Grid, Is.EqualTo(grid),
                "The standing mob must remember the grid under it.");

            // The other steps off in the same tick the grid goes, before its mover can notice.
            var origin = xforms.GetWorldPosition(grid);
            xforms.SetMapCoordinates(stander, new MapCoordinates(origin + new Vector2(0.5f, 2.5f), map.MapId));
            entMan.DeleteEntity(grid);

            foreach (var mob in new[] { floater, stander })
            {
                var momentum = entMan.GetComponent<WGGridMomentumComponent>(mob);
                Assert.That(momentum.Grid, Is.Null, "A mob still references a deleted grid as the one under it.");
                Assert.That(momentum.Left, Is.Null, "A mob still references a deleted grid as the one it left.");
                Assert.That(momentum.Frame, Is.EqualTo(Vector2.Zero), "A mob still carries a deleted grid's velocity.");
            }
        });

        // Send the cleared state to the client; a stale uid would log a resolve error here.
        await pair.RunTicksSync(10);

        await pair.CleanReturnAsync();
    }

    /// <summary>Holds the ship's speed against any passive braking, as thrust would, for some ticks.</summary>
    private static async Task HoldShip(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server,
        SharedPhysicsSystem physics, EntityUid grid, Vector2 velocity, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            await server.WaitPost(() => physics.SetLinearVelocity(grid, velocity));
            await server.WaitRunTicks(1);
        }
    }
}
