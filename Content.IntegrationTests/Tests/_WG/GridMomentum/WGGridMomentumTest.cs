#nullable enable
using System.Numerics;
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
            Assert.That(entMan.GetComponent<Content.Shared._WG.GridMomentum.WGGridMomentumComponent>(mob).Frame, Is.EqualTo(Vector2.Zero),
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
