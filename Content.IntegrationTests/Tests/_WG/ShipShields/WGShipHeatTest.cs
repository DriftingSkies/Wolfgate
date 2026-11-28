#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server._Mono.Detection;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WG.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using Content.Shared.Explosion.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks ship heat: hard and soft heat with the field up and down, venting, and what firing through the field costs.</summary>
[TestFixture]
public sealed class WGShipHeatTest
{
    [Test]
    public async Task RaisedFieldKeepsHardHeatAndShedsSoft()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var tick = Cooling(entities, emitterUid) * 1.5f;

            SetHeat(entities, emitterUid, 20000f);
            Assert.That(system.GetWolfgateShipHeat(grid).Heat, Is.EqualTo(20000f).Within(0.05f), "What the generator absorbs is the ship's heat.");
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            Assert.That(hull.Heat, Is.EqualTo(20000f).Within(0.05f), "The heat is held by the hull.");
            Assert.That(system.WolfgateHullCooling(emitterUid), Is.EqualTo(hull.Dissipation).And.GreaterThan(0f),
                "Under a generator the hull keeps its own cooling.");
            Assert.That(system.GetWolfgateShipHardHeat(grid), Is.EqualTo(20000f).Within(0.05f), "Heat that came through the field is hard.");
            system.Update(1.5f);
            Assert.That(Heat(entities, grid), Is.EqualTo(20000f).Within(0.05f), "Under a raised field that was just hit, hard heat stays.");

            system.AddWolfgateShipHeat(grid, tick * 3f);
            Assert.That(hull.Hard, Is.EqualTo(20000f).Within(0.05f), "Heat from the ship itself is soft.");
            system.Update(1.5f);
            Assert.That(Heat(entities, grid), Is.EqualTo(20000f + tick * 2f).Within(0.01f), "Soft heat cools under the raised field.");
            Assert.That(emitter.Damage, Is.EqualTo(hull.Heat * system.WolfgateGeneratorShare(emitterUid, emitter)).Within(0.05f),
                "The generator reads the hull's heat scaled to its own share of the ship's capacity.");
            Assert.That(ShipShieldsSystem.GetWolfgateShieldHealth(emitter), Is.EqualTo(1f - hull.Heat / system.GetWolfgateShipHeat(grid).Capacity).Within(0.001f),
                "So the field is as worn as the whole ship is hot.");
            var state = entities.System<WGShipShieldShuntSystem>().GetState(grid);
            Assert.That(state.HardHeat, Is.EqualTo((1f - state.Health) * 20000f / hull.Heat).Within(0.001f), "The panel shows the hard part of the gauge.");

            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            Assert.That(emitter.Shield, Is.Null);
            system.Update(1.5f);
            system.Update(1.5f);
            system.Update(1.5f);
            Assert.That(Heat(entities, grid), Is.EqualTo(20000f + tick * 2f - tick * 3f * WGShipShieldEffects.FieldDownCooling).Within(0.01f),
                "With the field down everything cools, soft heat first, at the field-down rate.");
            Assert.That(hull.Hard, Is.EqualTo(hull.Heat));

            Assert.That(system.SetWolfgateShieldEnabled(grid, true), Is.True);
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var held = Heat(entities, grid);
            system.Update(1.5f);
            Assert.That(Heat(entities, grid), Is.EqualTo(held).Within(0.05f), "Raised again, the field holds its hard heat.");
            hull.LastHit = timing.CurTime - TimeSpan.FromSeconds(WGShipShieldEffects.HardHeatHold + 1f);
            system.Update(1.5f);
            Assert.That(Heat(entities, grid), Is.EqualTo(held - tick).Within(0.01f), "Left alone long enough, hard heat cools under the field too.");
            var stats = entities.System<WGShipShieldShuntSystem>().GetState(grid).Stats!;
            Assert.That(stats.Vents, Is.True);
            Assert.That(stats.RechargePerSecond, Is.EqualTo(WGShipShieldEffects.VentRate(Cooling(entities, emitterUid),
                system.GetWolfgateShipHeat(grid).Capacity)).Within(0.01f));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VentShedsHeatFastWithFieldDownAndGunsSilent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var controls = entities.System<WGShipShieldShuntSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            var gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(emitter.UnpoweredBonus, Is.GreaterThan(1f));
            Assert.That(system.StartWolfgateVent(grid), Is.False, "A cold ship has nothing to vent.");
            Assert.That(controls.GetState(grid).CanVent, Is.False);

            var tick = WGShipShieldEffects.VentRate(Cooling(entities, emitterUid), system.GetWolfgateShipHeat(grid).Capacity) * 1.5f;
            SetHeat(entities, emitterUid, tick * 2.5f);
            Assert.That(controls.GetState(grid).CanVent, Is.True);
            Assert.That(system.StartWolfgateVent(grid), Is.True);
            Assert.That(system.IsWolfgateShipVenting(grid), Is.True);
            Assert.That(emitter.Shield, Is.Null, "A vent drops the field.");
            Assert.That(entities.HasComponent<WGShipVentingComponent>(grid), Is.True, "Radars are told the hull is venting.");
            Assert.That(Refused(entities, gun, user), Is.True, "A venting ship's guns can't fire.");
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.Zero, "A vent is not the collapse lock.");
            Assert.That(system.StartWolfgateVent(grid), Is.False);
            var state = controls.GetState(grid);
            Assert.That(state.CanVent, Is.False);
            Assert.That(state.VentSeconds, Is.EqualTo(4), "The panel counts the vent down.");
            Assert.That(state.RecoveryStatus, Is.EqualTo(WGShipShieldRecoveryStatus.Venting));
            var signature = new GetThermalSignatureEvent();
            entities.EventBus.RaiseLocalEvent(grid, ref signature);
            Assert.That(signature.Signature, Is.GreaterThanOrEqualTo(entities.GetComponent<WGShipHeatComponent>(grid).VentSignature),
                "A venting hull shows hot on long-range sensors.");

            system.Update(1.5f);
            Assert.That(Heat(entities, grid), Is.EqualTo(tick * 1.5f).Within(0.01f), "A vent cools at the generator's fast rate.");
            Assert.That(emitter.Shield, Is.Null, "The field stays down for as long as the vent runs.");
            Assert.That(Plumes(entities, grid), Is.GreaterThan(0), "Heat leaves the hull's edge where everyone can see it.");
            Assert.That(system.SetWolfgateShieldEnabled(grid, true), Is.True);
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null, "A vent can't be called off.");
            Assert.That(Refused(entities, gun, user), Is.True);

            for (var i = 0; i < 4 && emitter.Shield == null; i++)
                system.Update(1.5f);
            Assert.That(Heat(entities, grid), Is.Zero);
            Assert.That(system.IsWolfgateShipVenting(grid), Is.False, "The vent ends when no heat is left.");
            Assert.That(entities.HasComponent<WGShipVentingComponent>(grid), Is.False);
            Assert.That(emitter.Shield, Is.Not.Null, "The field comes back by itself.");
            Assert.That(Refused(entities, gun, user), Is.False);
            Assert.That(controls.GetState(grid).VentSeconds, Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NpcCrewVentsWhenHotAndLeftAlone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var capacity = system.GetWolfgateShipHeat(grid).Capacity;
            var longAgo = TimeSpan.FromSeconds(-600);

            SetHeat(entities, emitterUid, capacity * (ShipShieldsSystem.NpcVentHeat + 0.1f));
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            Assert.That(system.WantsWolfgateNpcVent(grid, true), Is.False, "A crew under fire keeps its field up.");
            Assert.That(system.WantsWolfgateNpcVent(grid, false), Is.True, "With nothing to fight it vents.");
            hull.LastHit = timing.CurTime + longAgo;
            Assert.That(system.WantsWolfgateNpcVent(grid, true), Is.True, "Hot and left alone, it drops the field to vent.");
            SetHeat(entities, emitterUid, capacity * (ShipShieldsSystem.NpcVentHeat - 0.1f));
            hull.LastHit = timing.CurTime + longAgo;
            Assert.That(system.WantsWolfgateNpcVent(grid, true), Is.False, "It doesn't drop the field for a little heat.");
            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            Assert.That(system.WantsWolfgateNpcVent(grid, true), Is.True, "With the field already down it has nothing to lose.");
            Assert.That(system.SetWolfgateShieldEnabled(grid, true), Is.True);

            var pilot = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<ShipSteererComponent>(pilot);
            // The crews are asked once a second.
            system.Update(1.01f);
            Assert.That(system.IsWolfgateShipVenting(grid), Is.True, "A ship an NPC is flying vents without being told.");
            entities.DeleteEntity(pilot);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NpcCrewHoldsFireUnderAHotFieldAndVentsAfterAnOverload()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var capacity = system.GetWolfgateShipHeat(grid).Capacity;
            var gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var pilot = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<ShipSteererComponent>(pilot);
            entities.AddComponent<ShipTargetingComponent>(pilot).Target = map.GridCoords;

            system.AddWolfgateShipHeat(grid, capacity * (ShipShieldsSystem.NpcHoldFireHeat + 0.1f));
            // The crews are asked once a second.
            system.Update(1.01f);
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            Assert.That(system.IsWolfgateShipVenting(grid), Is.False, "Soft heat goes by itself, so the crew keeps its field.");
            Assert.That(Refused(entities, gun, user), Is.True, "An NPC crew stops firing before its own guns fill a raised field.");
            entities.DeleteEntity(pilot);
            system.Update(1.01f);
            Assert.That(system.WantsWolfgateNpcHoldFire(grid), Is.True);
            Assert.That(Refused(entities, gun, user), Is.False, "A player crew makes that call itself.");

            hull.Hard = hull.Heat;
            Assert.That(system.WantsWolfgateNpcVent(grid, true), Is.True, "Hard heat past that mark can't be waited out, so it vents under fire.");

            system.AddWolfgateShipHeat(grid, capacity * 2f);
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null);
            Assert.That(hull.Collapsed, Is.True);
            Assert.That(system.WantsWolfgateNpcVent(grid, true), Is.False, "An overloaded ship can't vent.");
            emitter.OverloadAccumulator = 0f;
            hull.LastHit = timing.CurTime;
            Assert.That(system.WantsWolfgateNpcVent(grid, true), Is.True, "Once the overload ends it vents before fighting on, hit or not.");
            Assert.That(system.WantsWolfgateNpcVent(grid, true), Is.False, "It makes that call once.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AbsorbedHitsAddPartOfTheirDamageAndNoMoreThanAShareOfCapacity()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var capacity = system.GetWolfgateShipHeat(grid).Capacity;
            var cost = entities.GetComponent<WGShipShieldCostComponent>(emitterUid);
            var rate = cost.HitHeat;
            Assert.That(rate, Is.EqualTo(WGShipShieldEffects.HitHeat).And.GreaterThan(0f).And.LessThan(1f));

            var beam = entities.SpawnEntity(null, map.GridCoords);
            var damage = entities.EnsureComponent<HitscanBasicDamageComponent>(beam);
            damage.Damage = new DamageSpecifier();
            damage.Damage.DamageDict.Add("Heat", FixedPoint2.New(1000));
            cost.HitHeat = 1f;
            system.ApplyWolfgateHitscanImpact(emitter.Shield!.Value, beam, Vector2.Zero, 1f);
            var full = Heat(entities, grid);
            Assert.That(full, Is.GreaterThan(0f));
            cost.HitHeat = rate;
            system.ApplyWolfgateHitscanImpact(emitter.Shield!.Value, beam, Vector2.Zero, 1f);
            Assert.That(Heat(entities, grid) - full, Is.EqualTo(full * rate).Within(0.05f), "A field turns only part of what it absorbs into heat.");

            // A warhead many times the ship's capacity.
            damage.Damage = new DamageSpecifier();
            damage.Damage.DamageDict.Add("Heat", FixedPoint2.New(capacity * 8f));
            var before = Heat(entities, grid);
            system.ApplyWolfgateHitscanImpact(emitter.Shield!.Value, beam, Vector2.Zero, 1f);
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null, "One hit, however big, doesn't overrun a cool field.");
            Assert.That(Heat(entities, grid) - before, Is.EqualTo(capacity * WGShipShieldEffects.HitHeatCap).Within(capacity * 0.01f));

            SetHeat(entities, emitterUid, capacity * (1.05f - WGShipShieldEffects.HitHeatCap));
            system.ApplyWolfgateHitscanImpact(emitter.Shield!.Value, beam, Vector2.Zero, 1f);
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Null, "The same hit breaks a field that has less room left than one hit adds.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GeneratorWithoutPowerCoolsNothingOfItsOwn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            var power = entities.GetComponent<ApcPowerReceiverComponent>(emitterUid);
            power.Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            var capacity = system.GetWolfgateShipHeat(grid).Capacity;
            var own = system.WolfgateHullCooling(emitterUid);
            Assert.That(own, Is.GreaterThan(0f));

            system.AddWolfgateShipHeat(grid, 20000f);
            system.Update(1.5f);
            Assert.That(20000f - Heat(entities, grid), Is.EqualTo((emitter.HealPerSecond + own) * 1.5f * WGShipShieldEffects.FieldDownCooling).Within(0.01f),
                "With power, a lowered generator cools the ship at the field-down rate.");

            power.Powered = false;
            var before = Heat(entities, grid);
            system.Update(1.5f);
            Assert.That(before - Heat(entities, grid), Is.EqualTo(own * 1.5f * WGShipShieldEffects.FieldDownCooling).Within(0.01f),
                "Without power the generator cools nothing; the hull sheds only its own share.");
            Assert.That(system.GetWolfgateShipHeat(grid).Capacity, Is.EqualTo(capacity), "It still holds the heat.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OutpostGeneratorKeepsItsCoolingRule()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorPOI", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            Assert.That(entities.HasComponent<WGShipShieldCostComponent>(emitterUid), Is.False);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            SetHeat(entities, emitterUid, 20000f);
            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            Assert.That(emitter.Shield, Is.Null);
            system.Update(1.5f);
            Assert.That(20000f - Heat(entities, grid), Is.EqualTo(emitter.HealPerSecond * 1.5f).Within(0.01f),
                "A generator with no ship costs only cools fast in recharge mode.");
            Assert.That(entities.System<WGShipShieldShuntSystem>().GetState(grid).Stats!.Vents, Is.False);
            Assert.That(system.WolfgateHullCooling(emitterUid), Is.Zero, "An outpost's hull adds no cooling of its own.");
            Assert.That(system.StartWolfgateVent(grid), Is.False, "An outpost's generator has no vent.");

            var gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var (round, _) = Round(pair.Server.ResolveDependency<IPrototypeManager>(), entities.ComponentFactory);
            var before = Heat(entities, grid);
            Fire(entities, gun, user, entities.SpawnEntity(round, map.GridCoords));
            Assert.That(Heat(entities, grid), Is.EqualTo(before), "An outpost's guns don't heat it.");
            SetHeat(entities, emitterUid, system.GetWolfgateShipHeat(grid).Capacity * 0.99f);
            Assert.That(Refused(entities, gun, user), Is.False, "An outpost never holds its guns for heat.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GunsHeatTheShipFieldUpOrDownAndHoldWhenHot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var controls = entities.System<WGShipShieldShuntSystem>();
            var emitterUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            var gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            Assert.That(entities.HasComponent<FireControllableComponent>(gun), Is.True);
            var (round, damage) = Round(pair.Server.ResolveDependency<IPrototypeManager>(), entities.ComponentFactory);
            var cartridge = entities.SpawnEntity(round, map.GridCoords);

            Fire(entities, gun, user, cartridge);
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            Assert.That(hull.FireHeat, Is.GreaterThan(0f));
            Assert.That(Heat(entities, grid), Is.EqualTo(damage * hull.FireHeat).Within(0.01f),
                "With no field up a shot still heats the ship by a share of its damage.");
            Assert.That(hull.Hard, Is.Zero, "A ship's own fire is soft heat.");

            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            var cooled = Heat(entities, grid);
            Fire(entities, gun, user, cartridge);
            Assert.That(Heat(entities, grid) - cooled, Is.EqualTo(damage * hull.FireHeat).Within(0.01f),
                "A shot under the raised field heats the ship the same.");
            Assert.That(Refused(entities, gun, user), Is.False);

            var capacity = system.GetWolfgateShipHeat(grid).Capacity;
            SetHeat(entities, emitterUid, capacity * WGShipShieldEffects.GunHoldHeat - damage * hull.FireHeat / 2f);
            Fire(entities, gun, user, cartridge);
            Assert.That(Heat(entities, grid), Is.EqualTo(capacity * WGShipShieldEffects.GunHoldHeat).Within(0.05f), "A ship's own fire stops short of collapsing its field.");
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(Refused(entities, gun, user), Is.True, "Too hot, the guns hold fire.");
            Assert.That(controls.GetState(grid).GunsHeld, Is.True);
            Assert.That(system.GetWolfgateGunLockSeconds(grid), Is.Zero, "A heat hold is not the collapse lock.");

            SetHeat(entities, emitterUid, capacity * 0.9f);
            Assert.That(Refused(entities, gun, user), Is.True, "The guns stay held until the ship cools to the resume mark.");
            SetHeat(entities, emitterUid, capacity * WGShipShieldEffects.GunResumeHeat - 1f);
            Assert.That(Refused(entities, gun, user), Is.False);
            Assert.That(controls.GetState(grid).GunsHeld, Is.False);

            SetHeat(entities, emitterUid, capacity * WGShipShieldEffects.GunHoldHeat + 1f);
            Assert.That(Refused(entities, gun, user), Is.True);
            Assert.That(system.SetWolfgateShieldEnabled(grid, false), Is.True);
            Assert.That(Refused(entities, gun, user), Is.True, "Lowering the field doesn't cool the ship.");
            Assert.That(system.StartWolfgateVent(grid), Is.True);
            for (var i = 0; i < 30 && system.IsWolfgateShipVenting(grid); i++)
                system.Update(1.5f);
            Assert.That(system.IsWolfgateShipVenting(grid), Is.False);
            Assert.That(Refused(entities, gun, user), Is.False, "A vented ship fires again.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeflectorShipHeatsLikeAnyOtherAndBareHullHoldsItsOwnHeat()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();

            var (cold, capacity) = system.GetWolfgateShipHeat(grid);
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            Assert.That(cold, Is.Zero);
            Assert.That(hull.SinkCapacity, Is.GreaterThan(0f));
            Assert.That(capacity, Is.EqualTo(hull.Capacity + hull.SinkCapacity), "A hull with no generator holds its size plus its built-in sink.");
            system.AddWolfgateShipHeat(grid, 1500f);
            Assert.That(system.GetWolfgateShipHeat(grid), Is.EqualTo((1500f, hull.BareCapacity)), "A hull with no generator keeps its own heat.");
            system.Update(1f);
            // The hull cools once a second on a clock shared with earlier ticks, so up to two seconds may have passed.
            // A bare hull has no field, so it cools at the field-down rate like any ship with its field down.
            var cooled = hull.BareDissipation * WGShipShieldEffects.FieldDownCooling;
            Assert.That(hull.Heat, Is.InRange(1500f - cooled * 2f, 1500f - cooled));
            system.AddWolfgateShipHeat(grid, hull.BareCapacity * 2f);
            Assert.That(hull.Heat, Is.EqualTo(hull.BareCapacity), "A bare hull never holds more than its capacity.");

            var emitterUid = entities.SpawnEntity("WGShieldGeneratorDeflector", map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(Heat(entities, grid), Is.GreaterThan(hull.Capacity / 2f), "A generator installed on a hot hull takes on the hull's heat.");
            Assert.That(emitter.Damage, Is.EqualTo(hull.Heat * system.WolfgateGeneratorShare(emitterUid, emitter)).Within(0.05f));
            Assert.That(hull.Heat, Is.EqualTo(Heat(entities, grid)), "Reading the ship's heat carries the generator's cooling to the hull.");
            Assert.That(system.GetWolfgateShipHeat(grid).Capacity, Is.EqualTo(hull.Capacity +
                WGShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp)).Within(0.01f),
                "An installed generator takes the built-in sink's place.");
            var gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var (round, shot) = Round(pair.Server.ResolveDependency<IPrototypeManager>(), entities.ComponentFactory);
            var before = Heat(entities, grid);
            Fire(entities, gun, user, entities.SpawnEntity(round, map.GridCoords));
            Assert.That(Heat(entities, grid) - before, Is.EqualTo(shot * hull.FireHeat).Within(0.01f), "A deflector's ship heats from its guns like any other.");
            SetHeat(entities, emitterUid, system.GetWolfgateShipHeat(grid).Capacity * 0.99f);
            Assert.That(Refused(entities, gun, user), Is.True, "Too hot, a deflector's ship holds its guns too.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GunsHeatAndHoldOnAHullWithNoGenerator()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var controls = entities.System<WGShipShieldShuntSystem>();
            var gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var (round, damage) = Round(pair.Server.ResolveDependency<IPrototypeManager>(), entities.ComponentFactory);
            var cartridge = entities.SpawnEntity(round, map.GridCoords);

            Assert.That(Refused(entities, gun, user), Is.False);
            Fire(entities, gun, user, cartridge);
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            Assert.That(hull.Heat, Is.EqualTo(damage * hull.FireHeat).Within(0.01f), "A ship with no shield heats from its own guns.");
            Assert.That(controls.GetState(grid).CanVent, Is.True);

            hull.Heat = hull.BareCapacity * WGShipShieldEffects.GunHoldHeat - 1f;
            Fire(entities, gun, user, cartridge);
            Assert.That(hull.Heat, Is.EqualTo(hull.BareCapacity * WGShipShieldEffects.GunHoldHeat), "Gun heat stops at the hold mark.");
            Assert.That(Refused(entities, gun, user), Is.True, "Too hot, a bare hull holds its guns.");
            Assert.That(controls.GetState(grid).GunsHeld, Is.True);
            Assert.That(system.WantsWolfgateNpcVent(grid, true), Is.True, "An NPC crew vents a bare hull once its guns hold.");

            hull.Heat = hull.BareCapacity * WGShipShieldEffects.GunResumeHeat - 1f;
            Assert.That(Refused(entities, gun, user), Is.False, "Cooled to the resume mark, it fires again.");
            Assert.That(system.WantsWolfgateNpcVent(grid, true), Is.False, "In a fight its crew keeps firing while the guns still can.");
            Assert.That(system.WantsWolfgateNpcVent(grid, false), Is.True, "With nothing to fight it vents.");

            Assert.That(system.StartWolfgateVent(grid), Is.True);
            Assert.That(Refused(entities, gun, user), Is.True, "A venting hull's guns are silent.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShellBlastHeatsAtItsPeakTile()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var gun = entities.SpawnEntity("WeaponTurretHydra", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var shell = entities.SpawnEntity("220mmBulletHE", map.GridCoords);
            var explosive = entities.GetComponent<ExplosiveComponent>(shell);
            var perIntensity = (float) prototypes.Index<ExplosionPrototype>(explosive.ExplosionType).DamagePerIntensity.GetTotal();
            var hit = (float) entities.GetComponent<ProjectileComponent>(shell).Damage.GetTotal();
            Assert.That(explosive.MaxIntensity, Is.LessThan(explosive.TotalIntensity));

            var ammo = new List<(EntityUid? Uid, IShootable Shootable)> { (shell, entities.GetComponent<AmmoComponent>(shell)) };
            var ev = new GunShotEvent(user, ammo, entities.GetComponent<TransformComponent>(gun).Coordinates);
            entities.EventBus.RaiseLocalEvent(gun, ref ev);
            var hull = entities.GetComponent<WGShipHeatComponent>(map.Grid.Owner);
            Assert.That(hull.Heat, Is.EqualTo((hit + explosive.MaxIntensity * perIntensity) * hull.FireHeat).Within(0.01f),
                "A shell's blast heats its ship at the damage of its peak tile, not its whole spread.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BareHullHoldsAndShedsMoreTheBiggerItIs()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var maps = entities.System<SharedMapSystem>();
            var tiles = maps.GetAllTiles(grid, map.Grid.Comp).Count();

            system.GetWolfgateShipHeat(grid);
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            Assert.That(hull.Capacity, Is.EqualTo(WGShipShieldEffects.HullCapacity(hull.BaseCapacity, hull.CapacityPerTile,
                hull.MaxCapacity, tiles)).Within(0.01f));
            Assert.That(hull.Dissipation, Is.EqualTo(hull.Capacity / hull.CoolSeconds).Within(0.01f));
            var small = hull.Capacity;

            var plating = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            for (var x = 20; x < 30; x++)
            {
                for (var y = 20; y < 24; y++)
                    maps.SetTile(grid, map.Grid.Comp, new Vector2i(x, y), plating);
            }
            // Hulls are measured again on the once-a-second pass.
            system.Update(1.01f);
            Assert.That(hull.Capacity, Is.EqualTo(WGShipShieldEffects.HullCapacity(hull.BaseCapacity, hull.CapacityPerTile,
                hull.MaxCapacity, tiles + 40)).Within(0.01f).And.GreaterThan(small), "A bigger hull holds more heat.");
            Assert.That(hull.Dissipation, Is.EqualTo(hull.Capacity / hull.CoolSeconds).Within(0.01f), "And sheds it faster.");
            Assert.That(system.GetWolfgateShipHeat(grid).Capacity, Is.EqualTo(hull.BareCapacity));

            hull.MaxCapacity = small;
            maps.SetTile(grid, map.Grid.Comp, new Vector2i(40, 40), plating);
            system.Update(1.01f);
            Assert.That(hull.Capacity, Is.EqualTo(small), "However large, a bare hull stops at its ceiling.");

            // No generator cools its own capacity as fast as a bare hull, which never has a field to trap its heat.
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            foreach (var id in new[] { "ShieldGeneratorSmall", "ShieldGeneratorMedium", "ShieldGenerator", "ShieldGeneratorTSFCapital", "WGShieldGeneratorDeflector" })
            {
                Assert.That(prototypes.Index<EntityPrototype>(id).TryGetComponent<ShipShieldEmitterComponent>(out var emitter, entities.ComponentFactory), Is.True);
                var stats = WGShipShieldEffects.EffectiveCapacity(emitter!.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp);
                Assert.That(stats / emitter.HealPerSecond, Is.GreaterThan(hull.CoolSeconds / WGShipShieldEffects.FieldDownCooling),
                    $"{id} sheds a full load slower than a bare hull.");
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ShieldGeneratorTSFCapital", 15f)]
    [TestCase("ShieldGeneratorSmall", 30f)]
    [TestCase("ShieldGeneratorMedium", 45f)]
    public async Task EachGeneratorHoldsHardHeatForItsOwnTime(string generator, float hold)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            var emitterUid = entities.SpawnEntity(generator, map.GridCoords);
            var emitter = entities.GetComponent<ShipShieldEmitterComponent>(emitterUid);
            entities.GetComponent<ApcPowerReceiverComponent>(emitterUid).Powered = true;
            system.Update(1.5f);
            Assert.That(emitter.Shield, Is.Not.Null);
            Assert.That(entities.GetComponent<WGShipShieldCostComponent>(emitterUid).HardHeatHold, Is.EqualTo(hold));
            Assert.That(entities.System<WGShipShieldShuntSystem>().GetState(grid).Stats!.HardHeatHold, Is.EqualTo(hold));

            SetHeat(entities, emitterUid, 20000f);
            Assert.That(system.GetWolfgateShipHardHeat(grid), Is.EqualTo(20000f).Within(0.05f));
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            hull.LastHit = timing.CurTime - TimeSpan.FromSeconds(hold - 2f);
            system.Update(1.5f);
            Assert.That(Heat(entities, grid), Is.EqualTo(20000f).Within(0.05f), "Inside its hold the field keeps its hard heat.");
            hull.LastHit = timing.CurTime - TimeSpan.FromSeconds(hold + 1f);
            system.Update(1.5f);
            Assert.That(Heat(entities, grid), Is.EqualTo(20000f - Cooling(entities, emitterUid) * 1.5f).Within(0.01f), "Past its hold the hard heat cools.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BareHullShowsItsHeatAndVentsWithNoGenerator()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var controls = entities.System<WGShipShieldShuntSystem>();

            Assert.That(controls.GetState(null).Hull, Is.False, "A console with no ship has no heat to show.");
            var state = controls.GetState(grid);
            Assert.That(state.Available, Is.False);
            Assert.That(state.Hull, Is.True, "A ship with no generator still reports its hull.");
            Assert.That(state.Heat, Is.Zero);
            Assert.That(state.CanVent, Is.False, "A cold hull has nothing to vent.");

            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            var cold = new GetThermalSignatureEvent();
            entities.EventBus.RaiseLocalEvent(grid, ref cold);
            system.AddWolfgateShipHeat(grid, hull.BareCapacity / 2f);
            state = controls.GetState(grid);
            Assert.That(state.Heat, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(state.CanVent, Is.True);
            var hot = new GetThermalSignatureEvent();
            entities.EventBus.RaiseLocalEvent(grid, ref hot);
            Assert.That(hull.HeatSignature, Is.GreaterThan(0f));
            Assert.That(hot.Signature - cold.Signature, Is.EqualTo(hull.Heat * hull.HeatSignature).Within(1f),
                "A hot hull glows on long-range sensors by the heat it holds.");
            Assert.That(system.StartWolfgateVent(grid), Is.True);
            Assert.That(entities.HasComponent<WGShipVentingComponent>(grid), Is.True);
            var venting = new GetThermalSignatureEvent();
            entities.EventBus.RaiseLocalEvent(grid, ref venting);
            Assert.That(venting.Signature - hot.Signature, Is.EqualTo(hull.VentSignature).Within(1f), "A vent flares on top of the heat.");
            var rate = WGShipShieldEffects.VentRate(hull.BareDissipation, hull.BareCapacity);
            Assert.That(rate, Is.GreaterThan(hull.BareDissipation * WGShipShieldEffects.FieldDownCooling), "A vent beats a bare hull's field-down rate.");
            Assert.That(controls.GetState(grid).VentSeconds, Is.EqualTo((int) MathF.Ceiling(hull.Heat / rate)));

            var before = hull.Heat;
            system.Update(1f);
            system.Update(1f);
            Assert.That(before - hull.Heat, Is.GreaterThanOrEqualTo(MathF.Min(before, rate)), "A venting hull cools at its vent rate.");
            for (var i = 0; i < 10 && hull.Venting; i++)
                system.Update(1f);
            Assert.That(hull.Heat, Is.Zero);
            Assert.That(system.IsWolfgateShipVenting(grid), Is.False);
            Assert.That(entities.HasComponent<WGShipVentingComponent>(grid), Is.False, "The radar mark goes with the vent.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GeneratorsShareTheHullsHeatAndOnlyOneCoolsIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var system = entities.System<ShipShieldsSystem>();
            var activeUid = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var active = entities.GetComponent<ShipShieldEmitterComponent>(activeUid);
            entities.GetComponent<ApcPowerReceiverComponent>(activeUid).Powered = true;
            system.Update(1.5f);
            Assert.That(active.Shield, Is.Not.Null);
            // Nothing new bolts down beside a generator; a hull saved with two still carries both.
            var spareUid = WGShipShieldLimitTest.SpawnSavedGenerator(entities, "ShieldGeneratorSmall", map.GridCoords);
            var spare = entities.GetComponent<ShipShieldEmitterComponent>(spareUid);
            entities.GetComponent<ApcPowerReceiverComponent>(spareUid).Powered = false;

            system.AddWolfgateShipHeat(grid, 20000f);
            var hull = entities.GetComponent<WGShipHeatComponent>(grid);
            Assert.That(hull.Heat, Is.EqualTo(20000f));
            var share = system.WolfgateGeneratorShare(activeUid, active);
            Assert.That(share, Is.InRange(0.1f, 0.99f), "A ship generator supplies part of the ship's capacity; the hull supplies the rest.");
            Assert.That(active.Damage, Is.EqualTo(20000f * share).Within(0.05f), "The generator holding the field reads the hull's heat in its own range.");
            system.Update(1.5f);
            system.Update(0.01f);
            Assert.That(hull.Heat, Is.EqualTo(20000f - Cooling(entities, activeUid) * 1.5f).Within(0.01f),
                "A spare generator is no second heat sink: only the one holding the field cools the hull.");
            Assert.That(active.Damage, Is.EqualTo(hull.Heat * share).Within(0.05f));
            Assert.That(spare.Damage, Is.EqualTo(active.Damage).Within(0.05f), "Every generator on the hull reads the same heat.");

            entities.System<SharedTransformSystem>().Unanchor(spareUid, entities.GetComponent<TransformComponent>(spareUid));
            system.Update(0.01f);
            Assert.That(spare.Damage, Is.Zero, "A generator taken off the hull leaves the heat behind.");
            Assert.That(active.Damage, Is.EqualTo(hull.Heat * share).Within(0.05f));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A ship's heat in heat points, however its generator scales it.</summary>
    private static float Heat(IEntityManager entities, EntityUid grid)
    {
        return entities.System<ShipShieldsSystem>().GetWolfgateShipHeat(grid).Heat;
    }

    /// <summary>Heat per second a generator's ship sheds: the generator's rate plus the hull's part.</summary>
    private static float Cooling(IEntityManager entities, EntityUid uid)
    {
        return entities.GetComponent<ShipShieldEmitterComponent>(uid).HealPerSecond + entities.System<ShipShieldsSystem>().WolfgateHullCooling(uid);
    }

    /// <summary>Sets a ship's heat through its generator's damage, the way outside tools reach it.</summary>
    private static void SetHeat(IEntityManager entities, EntityUid uid, float heat)
    {
        var emitter = entities.GetComponent<ShipShieldEmitterComponent>(uid);
        emitter.Damage = heat * entities.System<ShipShieldsSystem>().WolfgateGeneratorShare(uid, emitter);
    }

    [Test]
    public async Task AGunsOwnHeatReplacesTheDamageShare()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var grid = map.Grid.Owner;
            var gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var (round, damage) = Round(prototypes, entities.ComponentFactory);
            var own = entities.AddComponent<WGShipGunHeatComponent>(gun);
            own.DamageShare = 0.1f;
            Assert.That(entities.EnsureComponent<WGShipHeatComponent>(grid).FireHeat, Is.Not.EqualTo(own.DamageShare));

            Fire(entities, gun, user, entities.SpawnEntity(round, map.GridCoords));
            Assert.That(Heat(entities, grid), Is.EqualTo(damage * 0.1f).Within(0.01f), "A gun with its own share heats by that, not the hull's.");

            own.HeatPerShot = 7f;
            var before = Heat(entities, grid);
            Fire(entities, gun, user, entities.SpawnEntity(round, map.GridCoords));
            Assert.That(Heat(entities, grid) - before, Is.EqualTo(7f).Within(0.01f), "A gun's own heat per shot wins over any share.");

            // The Polaris guns carry their own heat; the Flutter, a Phalanx child, sets its own instead of the PD value.
            foreach (var (id, heat) in new[] { ("WeaponLaserTurretL1Phalanx", 3f), ("WeaponTurretAk820", 140f), ("WeaponTurretAk800", 140f),
                         ("WeaponTurretHades", 1500f), ("WGAsfTurretFlutter", 62.5f) })
            {
                Assert.That(prototypes.Index<EntityPrototype>(id).TryGetComponent<WGShipGunHeatComponent>(out var set, entities.ComponentFactory), Is.True, id);
                Assert.That(set!.HeatPerShot, Is.EqualTo(heat), id);
            }

            // Rapid-fire cannons heat at a lower share of their damage.
            foreach (var id in new[] { "WeaponTurretRAC30", "WeaponTurretAC-35" })
            {
                Assert.That(prototypes.Index<EntityPrototype>(id).TryGetComponent<WGShipGunHeatComponent>(out var set, entities.ComponentFactory), Is.True, id);
                Assert.That(set!.DamageShare, Is.EqualTo(0.15f), id);
                Assert.That(set.HeatPerShot, Is.Null, id);
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("SandboxHEMissile")]
    [TestCase("MissileCartridge250mm")]
    [TestCase("VanykCartridgeUnguided")]
    public async Task FiringMissilesHeatsNothing(string missile)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var grid = map.Grid.Owner;
            var gun = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var cartridge = entities.SpawnEntity(missile, map.GridCoords);
            var shot = entities.GetComponent<CartridgeAmmoComponent>(cartridge).Prototype;
            Assert.That(pair.Server.ResolveDependency<IPrototypeManager>().Index(shot).TryGetComponent<WGShipMissileComponent>(out _, entities.ComponentFactory),
                Is.True, $"{shot} is marked as a missile.");

            Fire(entities, gun, user, cartridge);
            Assert.That(Heat(entities, grid), Is.Zero, "A ship's own missiles add no heat.");
            var (round, _) = Round(pair.Server.ResolveDependency<IPrototypeManager>(), entities.ComponentFactory);
            Fire(entities, gun, user, entities.SpawnEntity(round, map.GridCoords));
            Assert.That(Heat(entities, grid), Is.GreaterThan(0f), "Its guns still do.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A plain cartridge and the damage of the round it fires.</summary>
    private static (string Cartridge, float Damage) Round(IPrototypeManager prototypes, IComponentFactory factory)
    {
        foreach (var prototype in prototypes.EnumeratePrototypes<EntityPrototype>().OrderBy(p => p.ID))
        {
            if (prototype.Abstract || !prototype.TryGetComponent<CartridgeAmmoComponent>(out var cartridge, factory) ||
                !prototypes.TryIndex<EntityPrototype>(cartridge.Prototype, out var round) ||
                round.TryGetComponent<ExplosiveComponent>(out _, factory) || round.TryGetComponent<ProjectileSpreadComponent>(out _, factory) ||
                !round.TryGetComponent<ProjectileComponent>(out var projectile, factory))
                continue;
            var damage = (float) projectile.Damage.GetTotal();
            // Heavy enough that one shot's heat shows against a generator's capacity.
            if (damage >= 20f)
                return (prototype.ID, damage);
        }
        throw new InvalidOperationException("No plain cartridge prototype to fire.");
    }

    /// <summary>Reports one shot of a cartridge from a gun, as the gun system does after it fires.</summary>
    private static void Fire(IEntityManager entities, EntityUid gun, EntityUid user, EntityUid cartridge)
    {
        var ammo = new List<(EntityUid? Uid, IShootable Shootable)> { (cartridge, entities.GetComponent<CartridgeAmmoComponent>(cartridge)) };
        var ev = new GunShotEvent(user, ammo, entities.GetComponent<TransformComponent>(gun).Coordinates);
        entities.EventBus.RaiseLocalEvent(gun, ref ev);
    }

    /// <summary>How many heat plumes are leaving a hull.</summary>
    private static int Plumes(IEntityManager entities, EntityUid grid)
    {
        var count = 0;
        var query = entities.EntityQueryEnumerator<TimedDespawnComponent, MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out _, out var meta, out var transform))
        {
            if (meta.EntityPrototype?.ID == "WGEffectShipHeatVent" && transform.ParentUid == grid)
                count++;
        }
        return count;
    }

    private static bool Refused(IEntityManager entities, EntityUid gun, EntityUid user)
    {
        var ev = new AttemptShootEvent(user, null);
        entities.EventBus.RaiseLocalEvent(gun, ref ev);
        return ev.Cancelled;
    }
}
