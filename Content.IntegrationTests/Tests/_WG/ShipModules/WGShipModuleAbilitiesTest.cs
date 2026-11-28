using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server._WG.BoardingAction.Components;
using Content.Server._WG.Drydock;
using Content.Server._WG.ShipModules;
using Content.Server.Power.Components;
using Content.Shared._WG.ShipModules;
using Content.Shared.Damage;
using Content.Shared.Lathe;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Research.Prototypes;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.ShipModules;

/// <summary>
/// Checks how each ship module is unlocked, and the phase skimmer, ammo feeder, gyros and fortress shield on a test
/// hull.
/// </summary>
[TestFixture]
public sealed class WGShipModuleAbilitiesTest
{
    private const string Skimmer = "WGShipModulePhaseSkimmer";
    private const string Feeder = "WGShipModuleAmmoFeeder";
    private const string Gyros = "WGShipModuleGunGyros";
    private const string Fortress = "WGShipModuleFortressShield";
    private static readonly EntProtoId Imprinter = "CircuitImprinter";

    [Test]
    public async Task EveryModuleSaysHowItIsUnlocked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.That(prototypes.Index(Imprinter).TryComp<LatheComponent>(out var lathe, factory), Is.True);

            foreach (var module in prototypes.EnumeratePrototypes<WGShipModulePrototype>())
            {
                var states = (module.Free ? 1 : 0) + (module.Technology != null ? 1 : 0) + (module.Unlisted ? 1 : 0);
                Assert.That(states, Is.EqualTo(1),
                    $"{module.ID} must be exactly one of free, researched or unlisted.");
                if (module.Free)
                    Assert.That(module.Kit, Is.Null, $"Free module {module.ID} names a kit.");
                if (module.Technology is not { } technology)
                    continue;

                Assert.That(module.Kit, Is.Not.Null, $"{module.ID} needs research but names no kit.");
                var kit = prototypes.Index(module.Kit!.Value);
                Assert.That(kit.TryComp<WGShipModuleKitComponent>(out var kitComp, factory), Is.True,
                    $"{kit.ID} isn't a module kit.");
                Assert.That(kitComp!.Module.Id, Is.EqualTo(module.ID), $"{kit.ID} fits another module.");

                var tech = prototypes.Index(technology);
                var unlocks = tech.RecipeUnlocks
                    .Where(r => prototypes.Index(r).Result == module.Kit)
                    .ToList();
                Assert.That(unlocks, Is.Not.Empty, $"{tech.ID} doesn't unlock {kit.ID}.");
                Assert.That(lathe!.DynamicPacks.Any(p => prototypes.Index(p).Recipes.Contains(unlocks[0])), Is.True,
                    $"The circuit imprinter can't print {unlocks[0]}.");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AbilitiesWorkOnAHull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var modules = entities.System<WGShipModuleSystem>();
        var transform = entities.System<SharedTransformSystem>();
        var grid = map.Grid.Owner;
        var tick = (float) timing.TickPeriod.TotalSeconds;
        int Ticks(float seconds) => (int) MathF.Ceiling(seconds / tick);
        EntityUid helm = default;
        EntityUid pilot = default;

        await server.WaitPost(() =>
        {
            helm = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(helm).NeedsPower = false;
            pilot = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<PilotComponent>(pilot);
        });
        await pair.RunTicksSync(Ticks(1f));

        // Phase skimmer: held hulls can't skim; a free one lands its range along the held direction, a charge spent.
        var skim = (WGPhaseSkimmerEffect) prototypes.Index<WGShipModulePrototype>(Skimmer).Effect;
        await server.WaitAssertion(() =>
        {
            modules.Fit(grid, 2, Skimmer);
            var bay = entities.GetComponent<WGShipModuleBayComponent>(grid);
            var prototype = prototypes.Index<WGShipModulePrototype>(Skimmer);
            entities.AddComponent<WGFtlDisruptedComponent>(grid).Sources.Add(helm);
            Assert.That(modules.GetLock(grid, bay, prototype, true, timing.CurTime), Is.EqualTo(WGShipModuleLock.Held));
            entities.RemoveComponent<WGFtlDisruptedComponent>(grid);

            entities.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.StrafeRight;
            var (start, rotation) = transform.GetWorldPositionRotation(grid);
            var mass = entities.GetComponent<PhysicsComponent>(grid).Mass;
            var range = skim.Range * MathF.Max(MathF.Min(1f, skim.ReferenceMass / mass), skim.MinScale);
            Assert.That(modules.TryActivate(grid, 2, pilot, helm), Is.True);
            var moved = transform.GetWorldPosition(grid) - start;
            Assert.That(moved.Length(), Is.EqualTo(range).Within(0.1f));
            Assert.That(Vector2.Dot(Vector2.Normalize(moved), rotation.RotateVec(Vector2.UnitX)), Is.GreaterThan(0.99f));
            Assert.That(modules.GetFitted(grid, 2)!.Charges, Is.EqualTo(prototype.MaxCharges - 1));
            modules.Unfit(grid, 2);
        });

        // Ammo feeder: ballistic guns fire twice as fast for its duration, a battery-fed cannon Mono types as ballistic
        // too, and a burst gun's pause after a burst is halved; energy guns are left alone.
        EntityUid kinetic = default;
        EntityUid cannon = default;
        EntityUid energy = default;
        var feeder = prototypes.Index<WGShipModulePrototype>(Feeder);
        var factor = ((WGAmmoFeederEffect) feeder.Effect).FireRateFactor;
        float RateMul(EntityUid gun)
        {
            var ev = new QueryFireRateMultiplierEvent(1f);
            entities.EventBus.RaiseLocalEvent(gun, ref ev);
            return ev.ReloadTimeMul;
        }

        await server.WaitPost(() =>
        {
            kinetic = entities.SpawnEntity("WeaponTurretL85Autocannon", map.GridCoords);
            cannon = entities.SpawnEntity("WeaponTurretHades", map.GridCoords);
            energy = entities.SpawnEntity("WeaponTurretType35", map.GridCoords);
            modules.Fit(grid, 0, Feeder);
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var feederSystem = entities.System<WGAmmoFeederSystem>();
            Assert.That(feederSystem.IsBallistic(kinetic), Is.True);
            Assert.That(feederSystem.IsBallistic(cannon), Is.True, "The battery-fed Hades wasn't counted as ballistic.");
            Assert.That(feederSystem.IsBallistic(energy), Is.False);
            Assert.That(modules.TryActivate(grid, 0, pilot, helm), Is.True);
            Assert.That(entities.HasComponent<WGAmmoFeederBoostedComponent>(kinetic), Is.True);
            Assert.That(entities.HasComponent<WGAmmoFeederBoostedComponent>(cannon), Is.True);
            Assert.That(entities.HasComponent<WGAmmoFeederBoostedComponent>(energy), Is.False);
            Assert.That(RateMul(kinetic), Is.EqualTo(1f / factor).Within(0.001f));
            Assert.That(RateMul(cannon), Is.EqualTo(1f / factor).Within(0.001f));
            Assert.That(RateMul(energy), Is.EqualTo(1f).Within(0.001f));

            // A finished burst's cooldown is cut by the feeder's share.
            var gun = entities.GetComponent<GunComponent>(cannon);
            Assert.That(gun.SelectedMode, Is.EqualTo(SelectiveFire.Burst));
            gun.BurstActivated = false;
            var next = gun.NextFire;
            var shot = new GunShotEvent(cannon, new(), map.GridCoords);
            entities.EventBus.RaiseLocalEvent(cannon, ref shot);
            var saved = (next - gun.NextFire).TotalSeconds;
            Assert.That(saved, Is.EqualTo(gun.BurstCooldown * (1f - 1f / factor)).Within(0.01));
        });
        await pair.RunTicksSync(Ticks((float) feeder.Duration.TotalSeconds + 0.5f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<WGAmmoFeederBoostedComponent>(kinetic), Is.False, "The feeder never wound down.");
            Assert.That(RateMul(kinetic), Is.EqualTo(1f).Within(0.001f));
            modules.Fit(grid, 1, Gyros);
            modules.Fit(grid, 2, Fortress);
        });
        await pair.RunTicksSync(Ticks(0.5f));

        // Gyros speed every gun's turn while fitted; the fortress needs a raised field.
        await server.WaitAssertion(() =>
        {
            var gyros = (WGGunGyrosEffect) prototypes.Index<WGShipModulePrototype>(Gyros).Effect;
            Assert.That(entities.System<WGGunGyrosSystem>().TurnFactor(kinetic, grid, 0), Is.EqualTo(gyros.TurnFactor));
            var bay = entities.GetComponent<WGShipModuleBayComponent>(grid);
            var fortress = prototypes.Index<WGShipModulePrototype>(Fortress);
            Assert.That(modules.GetLock(grid, bay, fortress, true, timing.CurTime), Is.EqualTo(WGShipModuleLock.NoField));
            Assert.That(modules.TryActivate(grid, 2, pilot, helm), Is.False);
            modules.Unfit(grid, 1);
        });
        await pair.RunTicksSync(2);

        // A kit module's drydock fitting uses up the kit carried aboard.
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<WGGunGyrosSystem>().TurnFactor(kinetic, grid, 0), Is.EqualTo(1f));
            var gyros = prototypes.Index<WGShipModulePrototype>(Gyros);
            Assert.That(modules.HasKitFor(grid, gyros), Is.False);
            var kit = entities.SpawnEntity("WGShipModuleKitGunGyros", map.GridCoords);
            Assert.That(modules.HasKitFor(grid, gyros), Is.True);
            entities.System<WGDrydockSystem>().ApplyItem(grid, new WGDrydockWorkItem
            {
                Kind = WGDrydockWorkKind.Upgrade,
                Target = grid,
                Upgrade = WGShipModuleFitting.Key(1, Gyros),
            });
            Assert.That(modules.FittedSlot(grid, Gyros), Is.EqualTo(1));
            Assert.That(entities.IsQueuedForDeletion(kit) || entities.Deleted(kit), Is.True, "The kit wasn't used up.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Mapping presets fit their modules as the ship spawns, in a named slot or the first free one, and go.</summary>
    [Test]
    public async Task PresetsFitTheirModules()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var modules = entities.System<WGShipModuleSystem>();
        var grid = map.Grid.Owner;
        var presets = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            presets.Add(entities.SpawnEntity("WGShipModulePresetGunGyros", map.GridCoords));
            presets.Add(entities.SpawnEntity("WGShipModulePresetGunGyros", map.GridCoords));
            var fortress = entities.CreateEntityUninitialized("WGShipModulePresetFortressShield", map.GridCoords);
            entities.GetComponent<WGShipModulePresetComponent>(fortress).Slot = 3;
            entities.InitializeAndStartEntity(fortress, doMapInit: true);
            presets.Add(fortress);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // The gyros take the first free slot, the duplicate is skipped and the fortress lands on hotkey 3.
            Assert.That(modules.GetFitted(grid, 0)?.Module.Id, Is.EqualTo(Gyros));
            Assert.That(modules.GetFitted(grid, 1), Is.Null);
            Assert.That(modules.GetFitted(grid, 2)?.Module.Id, Is.EqualTo(Fortress));
            Assert.That(presets.All(p => entities.Deleted(p)), Is.True, "A preset outlived its spawn.");

            foreach (var preset in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (preset.Abstract || !preset.TryComp<WGShipModulePresetComponent>(out var comp, entities.ComponentFactory))
                    continue;
                Assert.That(prototypes.HasIndex(comp.Module), Is.True, $"{preset.ID} names no module.");
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull an NPC flies feeds its guns while engaged, jets out of the path of an incoming round and skims away from a
    /// missile lock, with no player at the helm.
    /// </summary>
    [Test]
    public async Task NpcShipsFireTheirModules()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var modules = entities.System<WGShipModuleSystem>();
        var transform = entities.System<SharedTransformSystem>();
        var grid = map.Grid.Owner;
        var tick = (float) timing.TickPeriod.TotalSeconds;
        int Ticks(float seconds) => (int) MathF.Ceiling(seconds / tick);
        const string jets = "WGShipModuleManeuveringJets";
        int MaxCharges(string module) => prototypes.Index<WGShipModulePrototype>(module).MaxCharges;

        // A debug round (20000 damage, or none when harmless) straight at the hull's middle, a second out.
        void Fire(bool harmless)
        {
            var body = entities.GetComponent<PhysicsComponent>(grid);
            var center = transform.GetWorldPosition(grid) + body.LocalCenter;
            var round = entities.SpawnEntity("BulletDebug", new MapCoordinates(center - new Vector2(0f, 60f), map.MapId));
            if (harmless)
                entities.GetComponent<ProjectileComponent>(round).Damage = new DamageSpecifier();
            entities.System<SharedPhysicsSystem>().SetLinearVelocity(round, body.LinearVelocity + new Vector2(0f, 60f));
        }

        await server.WaitPost(() =>
        {
            var helm = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(helm).NeedsPower = false;
            var npc = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<ShipTargetingComponent>(npc);
            modules.Fit(grid, 0, jets);
            modules.Fit(grid, 1, Feeder);
            modules.Fit(grid, 2, Skimmer);
        });
        await pair.RunTicksSync(Ticks(1.5f));

        await server.WaitAssertion(() =>
        {
            Assert.That(modules.GetFitted(grid, 1)!.Active, Is.True, "The NPC never fed its guns.");
            Assert.That(modules.GetFitted(grid, 0)!.Charges, Is.EqualTo(MaxCharges(jets)));

            Fire(true);
        });
        await pair.RunTicksSync(Ticks(0.6f));

        await server.WaitAssertion(() =>
        {
            Assert.That(modules.GetFitted(grid, 0)!.Charges, Is.LessThan(MaxCharges(jets)), "The NPC never dodged the round.");
            Assert.That(modules.GetFitted(grid, 2)!.Charges, Is.EqualTo(MaxCharges(Skimmer)), "The NPC skimmed off a small round.");
        });
        await pair.RunTicksSync(Ticks(1.2f));

        // A big round sends it skimming rather than jetting.
        Vector2 before = default;
        await server.WaitAssertion(() =>
        {
            before = transform.GetWorldPosition(grid);
            Fire(false);
        });
        await pair.RunTicksSync(Ticks(0.6f));

        await server.WaitAssertion(() =>
        {
            Assert.That(modules.GetFitted(grid, 2)!.Charges, Is.EqualTo(MaxCharges(Skimmer) - 1), "The NPC never skimmed off the big round.");
            Assert.That((transform.GetWorldPosition(grid) - before).Length(), Is.GreaterThan(20f));
        });
        await pair.RunTicksSync(Ticks(1.2f));

        // So does a missile lock.
        await server.WaitAssertion(() =>
        {
            before = transform.GetWorldPosition(grid);
            var missile = entities.SpawnEntity(null, new MapCoordinates(before + new Vector2(100f, 0f), map.MapId));
            entities.AddComponent<TargetSeekingComponent>(missile).CurrentTarget = grid;
        });
        await pair.RunTicksSync(Ticks(0.6f));

        await server.WaitAssertion(() =>
        {
            Assert.That(modules.GetFitted(grid, 2)!.Charges, Is.EqualTo(MaxCharges(Skimmer) - 2), "The NPC never skimmed off the lock.");
            Assert.That((transform.GetWorldPosition(grid) - before).Length(), Is.GreaterThan(20f));
        });
        await pair.CleanReturnAsync();
    }
}
