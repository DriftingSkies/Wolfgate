using System.Numerics;
using Content.Server._WG.Drydock;
using Content.Server._WG.ShipModules;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipModules;
using Content.Shared._WG.ShipShields;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.ShipModules;

/// <summary>Checks fitting ship modules into slots, their strip, charges and locks, and the maneuvering jets' burn.</summary>
[TestFixture]
public sealed class WGShipModuleTest
{
    private const string Jets = "WGShipModuleManeuveringJets";

    [Test]
    public void FittingKeysRoundTrip()
    {
        var key = WGShipModuleFitting.Key(2, Jets);
        Assert.That(WGShipModuleFitting.TryParse(key, out var slot, out var module), Is.True);
        Assert.That(slot, Is.EqualTo(2));
        Assert.That(module, Is.EqualTo(Jets));
        Assert.That(WGShipModuleFitting.TryParse("WGRefitThrusters", out _, out _), Is.False);
        Assert.That(WGShipModuleFitting.TryParse("wg-module:x:" + Jets, out _, out _), Is.False);
        Assert.That(WGShipModuleFitting.TryParse(null, out _, out _), Is.False);
    }

    [Test]
    public async Task JetsKickTheHullAndSpendCharges()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var modules = entities.System<WGShipModuleSystem>();
        var grid = map.Grid.Owner;
        EntityUid helm = default;
        EntityUid pilot = default;
        var burnTime = 0f;

        await server.WaitPost(() =>
        {
            helm = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(helm).NeedsPower = false;
            pilot = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.StrafeRight;
            modules.Fit(grid, 1, Jets);
        });
        // The power net updates receivers every 0.5 s and the strip every 0.25 s, so the helm reads powered within 1 s.
        await pair.RunTicksSync((int) MathF.Ceiling(1f / (float) timing.TickPeriod.TotalSeconds));

        await server.WaitAssertion(() =>
        {
            var jets = prototypes.Index<WGShipModulePrototype>(Jets);
            var effect = (WGManeuveringJetsEffect) jets.Effect;
            burnTime = effect.BurnTime;

            // The strip shows the one filled slot, full and free.
            var ship = entities.GetComponent<WGShipModulesComponent>(grid);
            Assert.That(ship.Slots, Has.Count.EqualTo(1));
            Assert.That(ship.Slots[0].Slot, Is.EqualTo(1));
            Assert.That(ship.Slots[0].Module.Id, Is.EqualTo(Jets));
            Assert.That(ship.Slots[0].Charges, Is.EqualTo(3));
            Assert.That(ship.Slots[0].Lock, Is.EqualTo(WGShipModuleLock.None));
            Assert.That(modules.TryActivate(grid, 0, pilot, helm), Is.False, "An empty slot fired.");

            // Nothing held: no burn, no charge spent.
            var fitted = modules.GetFitted(grid, 1)!;
            entities.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.None;
            Assert.That(modules.TryActivate(grid, 1, pilot, helm), Is.False);
            Assert.That(fitted.Charges, Is.EqualTo(3));

            // No powered helm, no firing.
            var bay = entities.GetComponent<WGShipModuleBayComponent>(grid);
            Assert.That(modules.GetLock(grid, bay, jets, false, timing.CurTime), Is.EqualTo(WGShipModuleLock.Unpowered));

            // A field seen a moment ago still locks the jets.
            bay.LastShielded = timing.CurTime;
            Assert.That(modules.GetLock(grid, bay, jets, true, timing.CurTime), Is.EqualTo(WGShipModuleLock.Shields));
            bay.LastShielded = null;

            // A deflector's partial field leaves the jets free; any other field holds the hull.
            var emitter = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<WGShipShieldDeflectorComponent>(emitter);
            var field = entities.AddComponent<ShipShieldedComponent>(grid);
            field.Shield = emitter;
            field.Source = emitter;
            Assert.That(modules.GetLock(grid, bay, jets, true, timing.CurTime), Is.EqualTo(WGShipModuleLock.None),
                "The deflector locked the jets.");
            entities.RemoveComponent<WGShipShieldDeflectorComponent>(emitter);
            Assert.That(modules.GetLock(grid, bay, jets, true, timing.CurTime), Is.EqualTo(WGShipModuleLock.Shields));
            entities.RemoveComponent<ShipShieldedComponent>(grid);
            entities.DeleteEntity(emitter);
            bay.LastShielded = null;

            // With the field lowered by hand, a vent or an overload still holds the jets.
            entities.AddComponent<WGShipVentingComponent>(grid);
            Assert.That(modules.GetLock(grid, bay, jets, true, timing.CurTime), Is.EqualTo(WGShipModuleLock.Venting));
            entities.RemoveComponent<WGShipVentingComponent>(grid);
            entities.AddComponent<WGShipOverloadedComponent>(grid);
            Assert.That(modules.GetLock(grid, bay, jets, true, timing.CurTime), Is.EqualTo(WGShipModuleLock.Overloaded));
            entities.RemoveComponent<WGShipOverloadedComponent>(grid);

            entities.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.StrafeRight;
            Assert.That(modules.TryActivate(grid, 1, pilot, helm), Is.True);
            Assert.That(fitted.Charges, Is.EqualTo(2));
            Assert.That(entities.GetComponent<WGShipModulesComponent>(grid).Slots[0].Charges, Is.EqualTo(2));
            Assert.That(modules.TryActivate(grid, 1, pilot, helm), Is.False, "The cooldown was ignored.");
        });
        // An unmanned grid is braked hard by GridSettle afterwards, so measure as it ends.
        await pair.RunTicksSync((int) MathF.Ceiling(burnTime / (float) timing.TickPeriod.TotalSeconds) + 1);

        await server.WaitAssertion(() =>
        {
            // The whole burn has landed along the held direction; damping takes a little back.
            // The one-tile test grid is laden by its own machines, so the kick is the rated one scaled for the hull.
            var body = entities.GetComponent<PhysicsComponent>(grid);
            var velocity = body.LinearVelocity;
            var effect = (WGManeuveringJetsEffect) prototypes.Index<WGShipModulePrototype>(Jets).Effect;
            var scale = entities.System<WGManeuveringJetsSystem>().KickScale(effect, grid, body);
            Assert.That(scale, Is.InRange(0.1f, 1f));
            var rated = effect.DeltaV * scale;
            var along = Vector2.Dot(velocity, entities.System<SharedTransformSystem>().GetWorldRotation(grid).RotateVec(Vector2.UnitX));
            Assert.That(along, Is.InRange(rated * 0.8f, rated * 1.02f));
            Assert.That(velocity.Length(), Is.EqualTo(along).Within(0.5f));
            Assert.That(entities.HasComponent<WGManeuveringJetsBurningComponent>(grid), Is.False, "The burn never ended.");

            // A module refills one charge when its timer runs out while the helm has power.
            modules.GetFitted(grid, 1)!.NextCharge = timing.CurTime;
        });
        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            Assert.That(modules.GetFitted(grid, 1)!.Charges, Is.EqualTo(3));
            Assert.That(entities.GetComponent<WGShipModulesComponent>(grid).Slots[0].Charges, Is.EqualTo(3));

            // A ship carries each module once: a second slot is refused, by the drydock's finished job too.
            var drydock = entities.System<WGDrydockSystem>();
            var fitting = new WGDrydockWorkItem
            {
                Kind = WGDrydockWorkKind.Upgrade,
                Target = grid,
                Upgrade = WGShipModuleFitting.Key(2, Jets),
            };
            Assert.That(modules.Fit(grid, 2, Jets), Is.False, "The same module went into two slots.");
            drydock.ApplyItem(grid, fitting);
            Assert.That(modules.GetFitted(grid, 2), Is.Null, "The drydock fitted the same module twice.");
            Assert.That(modules.Fit(grid, 1, Jets), Is.True, "Refitting a module's own slot was refused.");

            // Emptying the last slot drops the bay; a finished drydock fitting then fills the new slot.
            modules.Unfit(grid, 1);
            Assert.That(entities.HasComponent<WGShipModuleBayComponent>(grid), Is.False);
            drydock.ApplyItem(grid, fitting);
            Assert.That(modules.FittedSlot(grid, Jets), Is.EqualTo(2));
            Assert.That(entities.GetComponent<WGShipModulesComponent>(grid).Slots, Has.Count.EqualTo(1));
            Assert.That(entities.GetComponent<WGShipModulesComponent>(grid).Slots[0].Slot, Is.EqualTo(2));

            // With every slot emptied the ship loses its bay and strip.
            modules.Unfit(grid, 2);
            Assert.That(entities.HasComponent<WGShipModuleBayComponent>(grid), Is.False);
            Assert.That(entities.HasComponent<WGShipModulesComponent>(grid), Is.False);
        });

        // A shield generator switched on locks the jets with no field up, as while it recharges; switched off, it
        // frees them once the lockout passes.
        var sweepTicks = (int) MathF.Ceiling(0.5f / (float) timing.TickPeriod.TotalSeconds);
        EntityUid generator = default;
        await server.WaitPost(() =>
        {
            modules.Fit(grid, 1, Jets);
            generator = entities.SpawnEntity("ShieldGenerator", map.GridCoords);
        });
        await pair.RunTicksSync(sweepTicks);

        await server.WaitAssertion(() =>
        {
            var jets = prototypes.Index<WGShipModulePrototype>(Jets);
            var bay = entities.GetComponent<WGShipModuleBayComponent>(grid);
            Assert.That(entities.GetComponent<TransformComponent>(generator).Anchored, Is.True);
            Assert.That(entities.HasComponent<ShipShieldedComponent>(grid), Is.False, "The unpowered generator raised a field.");
            Assert.That(modules.GetLock(grid, bay, jets, true, timing.CurTime), Is.EqualTo(WGShipModuleLock.Shields),
                "A generator switched on left the jets free.");
            entities.GetComponent<ApcPowerReceiverComponent>(generator).PowerDisabled = true;
        });
        await pair.RunTicksSync(sweepTicks);

        await server.WaitAssertion(() =>
        {
            var jets = prototypes.Index<WGShipModulePrototype>(Jets);
            var bay = entities.GetComponent<WGShipModuleBayComponent>(grid);
            bay.LastShielded = null;
            Assert.That(modules.GetLock(grid, bay, jets, true, timing.CurTime), Is.EqualTo(WGShipModuleLock.None),
                "A generator switched off still locked the jets.");

            // Switched back on, it locks the jets at once, before the next sweep reads it.
            entities.GetComponent<ApcPowerReceiverComponent>(generator).PowerDisabled = false;
            entities.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.StrafeRight;
            Assert.That(modules.TryActivate(grid, 1, pilot, helm), Is.False,
                "A generator just switched on left the jets free until the next sweep.");
            Assert.That(modules.GetFitted(grid, 1)!.Charges, Is.EqualTo(3));
        });
        await pair.CleanReturnAsync();
    }
}
