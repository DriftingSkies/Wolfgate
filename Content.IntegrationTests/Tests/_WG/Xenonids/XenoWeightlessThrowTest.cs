#nullable enable
using System.Numerics;
using Content.Shared._WG.Xenonids.Movement;
using Content.Shared.Gravity;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WG.Xenonids;

/// <summary>A xeno thrown while weightless stops where the throw ends instead of drifting off.</summary>
[TestFixture]
[TestOf(typeof(XenoWeightlessThrowSystem))]
public sealed class XenoWeightlessThrowTest
{
    [Test]
    public async Task WeightlessThrowStops()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid drone = default;

        await server.WaitPost(() =>
        {
            drone = entMan.SpawnEntity("CMXenoDrone", new MapCoordinates(new Vector2(200, 200), map.MapId));
            server.System<ThrowingSystem>().TryThrow(drone, new Vector2(3, 0), 8f, animated: false);
        });

        await server.WaitAssertion(() =>
            Assert.That(server.System<SharedGravitySystem>().IsWeightless(drone), "the drone is out in space"));

        await pair.RunSeconds(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<ThrownItemComponent>(drone), Is.False);
            Assert.That(entMan.GetComponent<PhysicsComponent>(drone).LinearVelocity.Length(), Is.LessThan(0.01f));
        });

        await pair.CleanReturnAsync();
    }
}
