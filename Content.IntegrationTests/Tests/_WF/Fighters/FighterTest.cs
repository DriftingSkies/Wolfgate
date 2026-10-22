using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.EntitySystems;
using Content.Shared._WF.Fighters;
using Content.Shared.Atmos;
using Content.Shared.Power.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Fighters;

/// <summary>
/// The Broadsword: boarding, the nose gun standing in for the pilot's hands, cockpit air, flight and leaving.
/// </summary>
[TestFixture]
public sealed class FighterTest
{
    private const string Fighter = "WFFighterBroadsword";
    private const string Pilot = "MobHuman";

    [Test]
    public async Task BoardFlyAndLeave()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entities = server.ResolveDependency<IEntityManager>();
        var containers = entities.System<SharedContainerSystem>();
        var guns = entities.System<SharedGunSystem>();
        var atmos = entities.System<AtmosphereSystem>();
        var fighters = entities.System<SharedFighterSystem>();
        var appearance = entities.System<SharedAppearanceSystem>();

        EntityUid fighter = default;
        EntityUid pilot = default;

        await server.WaitPost(() =>
        {
            var space = new EntityCoordinates(map.MapUid, new System.Numerics.Vector2(50f, 50f));
            fighter = entities.SpawnEntity(Fighter, space);
            pilot = entities.SpawnEntity(Pilot, space);
            var comp = entities.GetComponent<FighterComponent>(fighter);
            Assert.That(containers.Insert(pilot, comp.PilotSlot));
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<FighterComponent>(fighter);
            Assert.That(comp.Pilot, Is.EqualTo(pilot));
            Assert.That(entities.GetComponent<FighterPilotComponent>(pilot).Fighter, Is.EqualTo(fighter));

            Assert.That(guns.TryGetGun(pilot, out var gun), "the pilot has no gun");
            Assert.That(gun.Owner, Is.EqualTo(fighter));
            Assert.That(gun.Comp.SoundGunshotModified, Is.Null, "the gun still plays a sound every round");
            Assert.That(gun.Comp.SoundGunshot, Is.Not.Null, "the fighter has no gunshot to play");

            var air = atmos.GetContainingMixture(pilot);
            Assert.That(air, Is.Not.Null);
            Assert.That(air!.Pressure, Is.EqualTo(Atmospherics.OneAtmosphere).Within(1f));
            Assert.That(air.GetMoles(Gas.Oxygen), Is.GreaterThan(0f));

            entities.GetComponent<FighterPilotComponent>(pilot).HeldButtons = ShuttleButtons.StrafeUp;
        });

        // Long enough to reach top speed.
        await server.WaitRunTicks(240);
        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<FighterComponent>(fighter);
            var speed = entities.GetComponent<PhysicsComponent>(fighter).LinearVelocity.Length();
            Assert.That(speed, Is.GreaterThan(1f));
            Assert.That(speed, Is.LessThanOrEqualTo(comp.MaxSpeed + 0.01f));
            Assert.That(appearance.TryGetData(fighter, FighterVisuals.MainBurn, out bool burning) && burning, "no main burn while thrusting");

            // Drive, the default, bleeds off speed once the stick is let go.
            entities.GetComponent<FighterPilotComponent>(pilot).HeldButtons = ShuttleButtons.None;
        });

        float released = 0;
        await server.WaitAssertion(() => released = entities.GetComponent<PhysicsComponent>(fighter).LinearVelocity.Length());
        await server.WaitRunTicks(20);
        await server.WaitAssertion(() =>
        {
            var speed = entities.GetComponent<PhysicsComponent>(fighter).LinearVelocity.Length();
            Assert.That(speed, Is.LessThan(released - 1f), "drive did not slow the fighter");
            Assert.That(appearance.TryGetData(fighter, FighterVisuals.RetroBurn, out bool retro) && retro, "no retro burn while drive slows");

            // Cruise keeps what it has.
            entities.GetComponent<FighterComponent>(fighter).FlightMode = FighterFlightMode.Cruise;
            released = speed;
        });
        await server.WaitRunTicks(20);
        await server.WaitAssertion(() =>
        {
            var speed = entities.GetComponent<PhysicsComponent>(fighter).LinearVelocity.Length();
            Assert.That(speed, Is.EqualTo(released).Within(0.1f), "cruise lost speed");

            // Park ignores thrust and stops.
            entities.GetComponent<FighterComponent>(fighter).FlightMode = FighterFlightMode.Park;
            entities.GetComponent<FighterPilotComponent>(pilot).HeldButtons = ShuttleButtons.StrafeUp;
        });

        await server.WaitRunTicks(300);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<PhysicsComponent>(fighter).LinearVelocity.Length(), Is.LessThan(0.01f));

            fighters.Eject((fighter, entities.GetComponent<FighterComponent>(fighter)));
            Assert.That(containers.IsEntityInContainer(pilot), Is.False);
            Assert.That(entities.HasComponent<FighterPilotComponent>(pilot), Is.False);
            Assert.That(entities.GetComponent<FighterComponent>(fighter).Pilot, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CellWaitsOutTheSalvoThenRefillsInAboutFourSeconds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var batteries = entities.System<BatterySystem>();

        EntityUid fighter = default;
        await server.WaitPost(() =>
        {
            fighter = entities.SpawnEntity(Fighter, new EntityCoordinates(map.MapUid, new System.Numerics.Vector2(50f, 50f)));
        });
        await server.WaitRunTicks(5);

        // The last round of a salvo empties the cell, the way a shot draws on it.
        await server.WaitPost(() => batteries.UseCharge(fighter, 140));
        await server.WaitRunTicks((int) (timing.TickRate * 0.15f));
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<BatteryComponent>(fighter).CurrentCharge, Is.Zero, "the cell charged mid-salvo"));

        // Self-recharge steps once a second, so four 35-charge steps fill it in three to four seconds.
        await server.WaitRunTicks((int) (timing.TickRate * 2.5f));
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<BatteryComponent>(fighter).CurrentCharge, Is.LessThan(140f), "the cell refilled in under three seconds"));

        await server.WaitRunTicks((int) (timing.TickRate * 1.8f));
        await server.WaitAssertion(() =>
            Assert.That(entities.GetComponent<BatteryComponent>(fighter).CurrentCharge, Is.EqualTo(140f).Within(0.01f), "the cell took longer than 4 s to refill"));

        await pair.CleanReturnAsync();
    }
}
