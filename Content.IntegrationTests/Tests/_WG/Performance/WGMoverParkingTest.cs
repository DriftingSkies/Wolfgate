#nullable enable
using System.Numerics;
using Content.Server._WG.Performance;
using Content.Server.Physics.Controllers;
using Content.Shared._WG.Performance;
using Content.Shared.Movement.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>A sleeping mover with nothing driving it leaves MoverController's list, and input, a push or the cvar bring it back.</summary>
[TestFixture]
public sealed class WGMoverParkingTest
{
    [Test]
    public async Task IdleMoverParksAndWakes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mover = entMan.System<MoverController>();
        var physics = entMan.System<SharedPhysicsSystem>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var map = await pair.CreateTestMap();

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
        });
        await server.WaitRunTicks(5);

        await Sleep();
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WGParkedMoverComponent>(mob), "An asleep mover with no input or player parks.");
            Assert.That(entMan.HasComponent<ActiveInputMoverComponent>(mob), Is.False, "A parked mover is off the update list.");
        });

        // A held direction wakes it on the next step, before the mover walk.
        await server.WaitPost(() =>
        {
            var input = entMan.GetComponent<InputMoverComponent>(mob);
            mover.SetVelocityDirection((mob, input), Direction.North, 0, true);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WGParkedMoverComponent>(mob), Is.False);
            Assert.That(entMan.HasComponent<ActiveInputMoverComponent>(mob), "Input puts a parked mover back.");
            Assert.That(entMan.GetComponent<PhysicsComponent>(mob).LinearVelocity.Length(), Is.GreaterThan(0f), "The woken mover walks.");
            var input = entMan.GetComponent<InputMoverComponent>(mob);
            mover.SetVelocityDirection((mob, input), Direction.North, 0, false);
        });
        await server.WaitRunTicks(5);

        // A push wakes the body, which brings the mover back.
        await Sleep();
        await server.WaitAssertion(() => Assert.That(entMan.HasComponent<WGParkedMoverComponent>(mob)));
        await server.WaitPost(() =>
        {
            physics.ApplyLinearImpulse(mob, new Vector2(10f, 0f), body: entMan.GetComponent<PhysicsComponent>(mob));
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<ActiveInputMoverComponent>(mob), "Waking the body puts a parked mover back.");
        });

        // Becoming a relay target puts it back on the list some other way, which ends the parking too.
        await Sleep();
        await server.WaitAssertion(() => Assert.That(entMan.HasComponent<WGParkedMoverComponent>(mob)));
        EntityUid driver = default;
        await server.WaitPost(() =>
        {
            driver = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2.5f, 0.5f)));
            mover.SetRelay(driver, mob);
        });
        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<ActiveInputMoverComponent>(mob), "A relay target is on the update list.");
            Assert.That(entMan.HasComponent<WGParkedMoverComponent>(mob), Is.False, "A mover back on the list isn't counted as parked.");
        });
        await server.WaitPost(() => entMan.DeleteEntity(driver));
        await server.WaitRunTicks(2);

        // Turning parking off brings every parked mover back.
        await Sleep();
        await server.WaitAssertion(() => Assert.That(entMan.HasComponent<WGParkedMoverComponent>(mob)));
        try
        {
            await server.WaitPost(() => cfg.SetCVar(WGPerformanceCVars.ParkIdleMovers, false));
            await server.WaitRunTicks(2);
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<WGParkedMoverComponent>(mob), Is.False);
                Assert.That(entMan.HasComponent<ActiveInputMoverComponent>(mob), "With parking off, parked movers return.");
            });
        }
        finally
        {
            // A pooled server must not keep parking off for the next test.
            await server.WaitPost(() => cfg.SetCVar(WGPerformanceCVars.ParkIdleMovers, true));
        }

        await server.WaitPost(() => entMan.DeleteEntity(mob));
        await pair.CleanReturnAsync();

        async Task Sleep()
        {
            await server.WaitPost(() =>
            {
                var body = entMan.GetComponent<PhysicsComponent>(mob);
                physics.SetLinearVelocity(mob, Vector2.Zero, body: body);
                physics.SetAwake((mob, body), false);
            });
            await server.WaitRunTicks(2);
        }
    }
}
