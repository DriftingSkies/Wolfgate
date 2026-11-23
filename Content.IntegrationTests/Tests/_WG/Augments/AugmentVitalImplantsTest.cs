using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._WG.Wolfmed;
using Content.Server._WG.Augments;
using Content.Server._WG.MeridianTraits.Hemophage;
using Content.Server._WG.Wolfmed.Life;
using Content.Server._WG.Wolfmed.Stasis;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Medical.Components;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WG.Augments;
using Content.Shared._WG.MeridianTraits.Hemophage;
using Content.Shared._WG.Wolfmed.Body;
using Content.Shared.Actions;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Emp;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// The nutriment pump, reviver and iatric monitor: where they fit, that a body gets them and reads them back, and
/// what each does.
/// </summary>
[TestFixture]
[TestOf(typeof(WGReviverSystem))]
public sealed class AugmentVitalImplantsTest
{
    private static readonly ProtoId<WGAugmentPrototype>[] Vital =
        ["WGAugmentNutrimentPump", "WGAugmentReviver", "WGAugmentIatricMonitor"];

    [Test]
    public async Task VitalImplantsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var timing = server.ResolveDependency<IGameTiming>();
        var augments = server.System<WGAugmentSystem>();
        var humanoids = server.System<SharedHumanoidAppearanceSystem>();
        var actions = server.System<SharedActionsSystem>();
        var ui = server.System<UserInterfaceSystem>();
        var hungers = server.System<HungerSystem>();
        var pumps = server.System<WGNutrimentPumpSystem>();
        var revivers = server.System<WGReviverSystem>();
        var life = server.System<WolfmedLifeSystem>();
        var revival = server.System<WolfmedRevivalSystem>();
        var mobState = server.System<MobStateSystem>();
        var emp = server.System<SharedEmpSystem>();
        var bloodstream = server.System<BloodstreamSystem>();
        var bodies = server.System<SharedBodySystem>();
        var organs = server.System<OrganHealthSystem>();
        var map = await WolfmedGameTest.CreateTestMap(pair);

        var mob = EntityUid.Invalid;
        var scanner = EntityUid.Invalid;
        void Scan()
        {
            var comp = entities.GetComponent<InstantActionComponent>(scanner);
            actions.PerformAction(mob, null, scanner, comp, comp.BaseEvent, timing.CurTime, false);
        }

        await server.WaitAssertion(() =>
        {
            var noTraits = new ProtoId<TraitPrototype>[0];
            var flesh = new Dictionary<WGAugmentSlot, WGAugmentChoice>();

            // The pump and the reviver need flesh; the monitor fits any head.
            Assert.That(WGAugmentRules.ValidateImplants("Human", flesh, Vital, noTraits, proto, factory), Is.EquivalentTo(Vital));
            Assert.That(WGAugmentRules.ValidateImplants("IPC", flesh, Vital, noTraits, proto, factory), Is.EquivalentTo(new[] { Vital[2] }));

            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            humanoids.LoadProfile(mob, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugmentImplants(Vital));
            Assert.That(augments.ReadImplants(mob), Is.EquivalentTo(Vital), "The implants put in don't read back.");

            // The pump leaves a fed body alone and takes a starving one back over the line, no further.
            var pump = entities.GetComponent<WGNutrimentPumpComponent>(mob);
            var hunger = entities.GetComponent<HungerComponent>(mob);
            Assert.That(hungers.GetHungerThreshold(hunger), Is.GreaterThan(HungerThreshold.Starving));
            Assert.That(pumps.Feed((mob, pump), hunger), Is.False);
            hungers.SetHunger(mob, 1f, hunger);
            Assert.That(hungers.GetHungerThreshold(hunger), Is.EqualTo(HungerThreshold.Starving));
            Assert.That(pumps.Feed((mob, pump), hunger));
            Assert.That(hungers.GetHunger(hunger), Is.EqualTo(1f + pump.Amount).Within(0.1f));
            for (var i = 0; i < 20 && pumps.Feed((mob, pump), hunger); i++)
            {
            }

            Assert.That(hungers.GetHungerThreshold(hunger), Is.EqualTo(HungerThreshold.Peckish));

            // The monitor's action opens the analyzer's window on its own body.
            var monitor = entities.GetComponent<WGIatricMonitorComponent>(mob);
            Assert.That(monitor.ActionEntity, Is.Not.Null, "The monitor gave its body no action.");
            scanner = monitor.ActionEntity.Value;
            Scan();
            Assert.That(ui.IsUiOpen(scanner, HealthAnalyzerUiKey.Key, mob));
            Assert.That(entities.GetComponent<HealthAnalyzerComponent>(scanner).ScannedEntity, Is.EqualTo(mob));
        });

        // It stays open: the action is in range of the body that holds it.
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.GetComponent<HealthAnalyzerComponent>(scanner);
            Assert.That(ui.IsUiOpen(scanner, HealthAnalyzerUiKey.Key, mob), "The readout closed by itself.");
            Assert.That(analyzer.ScannedEntity, Is.EqualTo(mob));
            Scan();
            Assert.That(ui.IsUiOpen(scanner, HealthAnalyzerUiKey.Key, mob), Is.False);
            Assert.That(analyzer.ScannedEntity, Is.Null, "The scan went on after its window closed.");

            // The reviver charges when the heart stops and holds the bleeding from then on, and an EMP knocks the
            // charge out and locks it.
            var reviver = entities.GetComponent<WGReviverComponent>(mob);
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.Null, "The reviver charged for a beating heart.");
            Assert.That(life.StartArrest(mob, "test"));
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.EqualTo(timing.CurTime + reviver.ChargeTime));
            Assert.That(entities.GetComponent<WolfmedStasisHoldComponent>(mob).HeldUntil,
                Is.EqualTo(reviver.NextShock + reviver.HoldTime), "The reviver didn't hold the bleeding.");
            Assert.That(reviver.Infused, Is.Zero, "The reviver ran blood into a body that had enough.");

            emp.DoEmpEffects(mob, 0f, System.TimeSpan.FromSeconds(30));
            Assert.That(reviver.NextShock, Is.Null);
            Assert.That(reviver.ReadyAt, Is.EqualTo(timing.CurTime + reviver.EmpLockout));
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.Null, "The reviver charged while an EMP had it locked.");

            // Short of the blood the paddles ask for, it runs its reserve in and the charge waits for it.
            for (var i = 0; i < 100 && life.GetBlood(mob) > 0.2f; i++)
            {
                bloodstream.TryModifyBloodLevel(mob, -10);
            }

            Assert.That(revival.GetRefusal(mob), Is.EqualTo(WolfmedRevivalSystem.NoBlood));
            var drained = life.GetBlood(mob);
            reviver.ReadyAt = System.TimeSpan.Zero;
            revival.ForcedRoll = 1f;
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.Not.Null, "The reviver gave up on a body its reserve could help.");
            Assert.That(reviver.Infused, Is.EqualTo(reviver.InfuseRate).Within(0.01f));
            Assert.That(life.GetBlood(mob), Is.GreaterThan(drained));
            reviver.NextShock = timing.CurTime;
            revivers.Tick((mob, reviver));
            Assert.That(reviver.ShocksGiven, Is.Zero, "The reviver shocked a body the paddles refuse.");

            // However long the charge waits, the bleeding stays held.
            var hold = entities.GetComponent<WolfmedStasisHoldComponent>(mob);
            hold.HeldUntil = timing.CurTime;
            reviver.NextShock = timing.CurTime - reviver.HoldTime - reviver.HoldTime;
            revivers.Tick((mob, reviver));
            Assert.That(hold.HeldUntil, Is.EqualTo(timing.CurTime + reviver.HoldTime), "The hold ran out while the charge waited.");
            for (var i = 0; i < 40 && reviver.ShocksGiven == 0; i++)
            {
                revivers.Tick((mob, reviver));
            }

            Assert.That(reviver.ShocksGiven, Is.EqualTo(1));
            Assert.That(revival.GetRefusal(mob), Is.Null);
            Assert.That(reviver.Infused, Is.LessThanOrEqualTo(reviver.Reserve));

            // It doesn't stop at the paddles' line: the whole reserve goes in, which is what wakes a restarted body.
            for (var i = 0; i < 20; i++)
            {
                revivers.Tick((mob, reviver));
            }

            Assert.That(reviver.Infused, Is.EqualTo(reviver.Reserve).Within(0.01f), "The reviver kept blood back from a body short of it.");

            // That shock and two more don't take, and it has to recharge.
            for (var i = 1; i < reviver.Shocks; i++)
            {
                Assert.That(reviver.ReadyAt, Is.EqualTo(System.TimeSpan.Zero));
                revivers.Tick((mob, reviver));
                Assert.That(reviver.NextShock, Is.Not.Null);
                reviver.NextShock = timing.CurTime;
                revivers.Tick((mob, reviver));
            }

            Assert.That(life.InArrest(mob));
            Assert.That(reviver.ReadyAt, Is.EqualTo(timing.CurTime + reviver.Recharge));
            Assert.That(reviver.Infused, Is.Zero);
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.Null, "The reviver charged while it was recharging.");

            // A shock that takes restarts the heart, and it recharges again.
            reviver.ReadyAt = System.TimeSpan.Zero;
            revival.ForcedRoll = 0f;
            revivers.Tick((mob, reviver));
            reviver.NextShock = timing.CurTime;
            revivers.Tick((mob, reviver));
            revival.ForcedRoll = null;
            Assert.That(life.InArrest(mob), Is.False, "The reviver's shock did not restart the heart.");
            Assert.That(mobState.IsDead(mob), Is.False);
            Assert.That(reviver.ReadyAt, Is.EqualTo(timing.CurTime + reviver.Recharge));

            // It doesn't spend a charge on a heart that is destroyed.
            var heart = bodies.GetBodyOrgans(mob).First(organ => entities.HasComponent<HeartComponent>(organ.Id)).Id;
            organs.SetHealth((heart, entities.GetComponent<WolfmedOrganComponent>(heart)), FixedPoint2.Zero);
            life.Tick(mob, 1f);
            Assert.That(life.InArrest(mob) || mobState.IsDead(mob));
            reviver.ReadyAt = System.TimeSpan.Zero;
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.Null, "The reviver charged for a destroyed heart.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A hemophage gets the reviver's blood for a wound, and none for what its tumor drank.</summary>
    [Test]
    public async Task ReviverHemophageTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var revivers = server.System<WGReviverSystem>();
        var hemophages = server.System<WGHemophageSystem>();
        var life = server.System<WolfmedLifeSystem>();
        var revival = server.System<WolfmedRevivalSystem>();
        var damageable = server.System<DamageableSystem>();
        var map = await WolfmedGameTest.CreateTestMap(pair);

        var mob = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.AddComponents(mob, proto.Index<TraitPrototype>("WGHemophagia").Components, false);
        });

        await pair.RunSeconds(2);

        WGReviverComponent reviver = default!;
        await server.WaitAssertion(() =>
        {
            var hemophage = entities.GetComponent<WGHemophageComponent>(mob);
            var blood = entities.GetComponent<BloodstreamComponent>(mob);
            reviver = entities.AddComponent<WGReviverComponent>(mob);

            // Starved under the paddles' line, with no wound: the tumor drank it, and the reviver has nothing for it.
            FixedPoint2 normal = blood.BloodMaxVolume;
            var full = (mob, hemophage, blood);
            hemophages.RemoveBlood(full, hemophages.TotalBlood(full) - normal * 0.22f);
            life.StartArrest(mob, "test");
            Assert.That(revival.GetRefusal(mob), Is.EqualTo(WolfmedRevivalSystem.NoBlood));
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.Null, "The reviver charged for a hemophage its tumor starved.");
            Assert.That(reviver.Infused, Is.Zero, "The reviver fed a tumor.");

            damageable.TryChangeDamage(mob, new DamageSpecifier(proto.Index<DamageTypePrototype>("Slash"), 25));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            // With a cut that bleeds, the blood is a wound's to replace.
            revivers.Tick((mob, reviver));
            Assert.That(reviver.NextShock, Is.Not.Null, "The reviver gave up on a hemophage that is bleeding.");
            Assert.That(reviver.Infused, Is.GreaterThan(0f), "The reviver kept its blood from a bleeding hemophage.");
        });

        await pair.CleanReturnAsync();
    }
}
