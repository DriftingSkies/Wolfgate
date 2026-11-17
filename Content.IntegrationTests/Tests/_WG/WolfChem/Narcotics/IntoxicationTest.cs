using System.Collections.Generic;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._WG.WolfChem.Narcotics;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Traits.Assorted;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// Levels pick tiers and decay, follow a blood drug, a long enough high ends in a crash, and only organic brains take
/// chemical doses.
/// </summary>
[TestFixture]
[TestOf(typeof(WGIntoxicationSystem))]
public sealed class IntoxicationTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wgDrugProfile
  id: WGIntoxTestProfile
  family: Stimulant
  decay: 1
  tiers:
  - min: 2
    speed: 1.2
  - min: 6
    speed: 0.5
    effects:
    - !type:HealthChange
      damage:
        types:
          Poison: 1
  crash:
    after: 2
    duration: 3
    speed: 0.8

- type: wgDrugProfile
  id: WGIntoxTestDamper
  family: Combat
  decay: 1
  tiers:
  - min: 2
    traits: [ Damper, HideDamage ]

- type: wgDrugProfile
  id: WGIntoxTestPhases
  family: Opioid
  decay: 0.5
  tiers:
  - min: 2
  - min: 5
  - min: 10

- type: wgDrugProfile
  id: WGIntoxTestBlood
  family: Stimulant
  decay: 0
  overdoseHalfLife: 10
  tiers:
  - min: 1
  - min: 4
  - min: 8

- type: reagent
  id: WGIntoxTestBloodDrug
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  metabolisms:
    Narcotic:
      metabolismRate: 0.01
      effects:
      - !type:WGDose
        profile: WGIntoxTestBlood
        perUnit: 1

- type: reagent
  id: WGIntoxTestMedicine
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  metabolisms:
    Narcotic:
      metabolismRate: 0.01
      effects:
      - !type:WGDose
        profile: WGIntoxTestBlood
        perUnit: 1
        dependenceOnly: true

- type: wgDrugProfile
  id: WGIntoxTestRush
  family: Opioid
  decay: 0
  rushGain: 2
  rushHalfLife: 10
  tiers:
  - min: 1
  - min: 5
  - min: 8

- type: reagent
  id: WGIntoxTestRushDrug
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  metabolisms:
    Narcotic:
      metabolismRate: 0.01
      effects:
      - !type:WGDose
        profile: WGIntoxTestRush
        perUnit: 1

- type: reagent
  id: WGIntoxTestReagent
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
";

    private const string Profile = "WGIntoxTestProfile";

    [Test]
    public async Task TierDecayCrashTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var sys = server.System<WGIntoxicationSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);

            sys.AddLevel(mob, Profile, 4f);
            var comp = entMan.GetComponent<WGIntoxicationComponent>(mob);
            Assert.That(sys.GetTier(comp, Profile), Is.EqualTo(0));
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(mob).WalkSpeedModifier,
                Is.EqualTo(1.2f).Within(0.001f), "A fed dose that reaches a tier applies its speed at once.");

            sys.Tick(1f);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Levels[Profile], Is.EqualTo(3f).Within(0.001f), "Decay should take 1 a second.");
                Assert.That(sys.GetSpeed(comp), Is.EqualTo(1.2f).Within(0.001f));
            });

            sys.Tick(1f);
            sys.Tick(1f);
            Assert.Multiple(() =>
            {
                Assert.That(sys.GetTier(comp, Profile), Is.EqualTo(-1));
                Assert.That(comp.Crashes.ContainsKey(Profile), Is.True, "Two seconds high should end in a crash.");
                Assert.That(sys.GetSpeed(comp), Is.EqualTo(0.8f).Within(0.001f));
            });

            sys.AddLevel(mob, Profile, 8f);
            Assert.That(sys.GetTier(comp, Profile), Is.EqualTo(1), "The top tier is the overdose.");
            Assert.That(sys.GetSpeed(comp), Is.EqualTo(0.5f).Within(0.001f), "A redose pauses the crash.");
            var poisonBefore = entMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.GetValueOrDefault("Poison");
            sys.Tick(1f);
            var poisonAfter = entMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.GetValueOrDefault("Poison");
            Assert.That(poisonAfter - poisonBefore, Is.EqualTo(FixedPoint2.New(1)), "Tier effects run each tick.");

            for (var i = 0; i < 30; i++)
            {
                sys.Tick(1f);
            }

            Assert.That(comp.Levels, Is.Empty);
            Assert.That(comp.Crashes, Is.Empty, "The crash should run out.");
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            var query = entMan.EntityQueryEnumerator<WGIntoxicationComponent>();
            Assert.That(query.MoveNext(out _, out _), Is.False, "The component should go once everything wore off.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The phase moves from overdose through heavy to light as the level decays.</summary>
    [Test]
    public async Task PhaseTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        const string profile = "WGIntoxTestPhases";

        await server.WaitAssertion(() =>
        {
            var sys = server.System<WGIntoxicationSystem>();
            var proto = server.ProtoMan.Index<WGDrugProfilePrototype>(profile);
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            sys.AddLevel(mob, profile, 12f);
            var comp = entMan.GetComponent<WGIntoxicationComponent>(mob);

            Assert.Multiple(() =>
            {
                Assert.That(proto.PhaseOf(-1), Is.EqualTo(WGDrugPhase.None));
                Assert.That(proto.PhaseOf(0), Is.EqualTo(WGDrugPhase.Light));
                Assert.That(proto.PhaseOf(1), Is.EqualTo(WGDrugPhase.Heavy));
                Assert.That(proto.PhaseOf(2), Is.EqualTo(WGDrugPhase.Overdose));
            });

            sys.Tick(0f);
            Assert.That(comp.Phases[profile], Is.EqualTo(WGDrugPhase.Overdose));

            sys.Tick(10f);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Levels[profile], Is.EqualTo(7f).Within(0.001f));
                Assert.That(comp.Phases[profile], Is.EqualTo(WGDrugPhase.Heavy));
            });

            sys.Tick(6f);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Levels[profile], Is.EqualTo(4f).Within(0.001f));
                Assert.That(comp.Phases[profile], Is.EqualTo(WGDrugPhase.Light));
            });

            sys.Tick(6f);
            Assert.That(comp.Phases.ContainsKey(profile), Is.False, "Below the first tier there is no phase.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RushTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        const string profile = "WGIntoxTestRush";

        await server.WaitAssertion(() =>
        {
            var sys = server.System<WGIntoxicationSystem>();
            var bloodstream = server.System<BloodstreamSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);

            void Inject(float units)
            {
                var dose = new Solution();
                dose.AddReagent("WGIntoxTestRushDrug", FixedPoint2.New(units));
                Assert.That(bloodstream.TryAddToChemicals(mob, dose), Is.True);
            }

            Inject(2f);
            var comp = entMan.EnsureComponent<WGIntoxicationComponent>(mob);
            sys.Tick(0f);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Levels[profile], Is.EqualTo(6f).Within(0.01f), "A fresh 2 rushes for twice as much again.");
                Assert.That(comp.Phases[profile], Is.EqualTo(WGDrugPhase.Heavy));
            });

            sys.Tick(10f);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Levels[profile], Is.EqualTo(4f).Within(0.01f), "The rush halves in its half-life.");
                Assert.That(comp.Phases[profile], Is.EqualTo(WGDrugPhase.Light));
            });

            Inject(4f);
            sys.Tick(0f);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Levels[profile], Is.LessThan(8f), "A rush stops short of the overdose tier.");
                Assert.That(comp.Phases[profile], Is.EqualTo(WGDrugPhase.Heavy));
            });

            Inject(4f);
            sys.Tick(0f);
            Assert.That(comp.Phases[profile], Is.EqualTo(WGDrugPhase.Overdose), "The blood alone still overdoses.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BloodLevelTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        const string profile = "WGIntoxTestBlood";
        const string drug = "WGIntoxTestBloodDrug";

        await server.WaitAssertion(() =>
        {
            var sys = server.System<WGIntoxicationSystem>();
            var bloodstream = server.System<BloodstreamSystem>();
            var solutions = server.System<SharedSolutionContainerSystem>();
            var chemicals = BloodstreamComponent.DefaultChemicalsSolutionName;

            FixedPoint2 Left(EntityUid mob)
            {
                Assert.That(solutions.TryGetSolution(mob, chemicals, out _, out var blood), Is.True);
                return blood!.GetTotalPrototypeQuantity(drug);
            }

            WGIntoxicationComponent Inject(EntityUid mob, float units, float grade)
            {
                var dose = new Solution();
                dose.AddReagent(drug, FixedPoint2.New(units), grade);
                Assert.That(bloodstream.TryAddToChemicals(mob, dose), Is.True);
                return entMan.EnsureComponent<WGIntoxicationComponent>(mob);
            }

            var cut = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var cutComp = Inject(cut, 4f, 0.5f);
            sys.Tick(0f);
            Assert.Multiple(() =>
            {
                Assert.That(cutComp.Levels[profile], Is.EqualTo(2f).Within(0.01f), "4 u at 50% in the blood is level 2.");
                Assert.That(cutComp.Fed, Is.Empty, "A blood drug feeds no level of its own.");
                Assert.That(cutComp.Phases[profile], Is.EqualTo(WGDrugPhase.Light));
            });

            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var comp = Inject(mob, 12f, 1f);
            sys.Tick(10f);
            Assert.That(Left(mob), Is.EqualTo(FixedPoint2.New(10)),
                "One overdose half-life flushes half of the 4 above the overdose tier, spread over the 12 in the blood.");
            sys.Tick(0f);
            Assert.That(comp.Levels[profile], Is.EqualTo(10f).Within(0.01f));

            sys.ReduceFamily(mob, WGDrugFamily.Stimulant, 4f);
            Assert.That(Left(mob), Is.EqualTo(FixedPoint2.New(6)), "An antagonist purges the drug from the blood.");
            sys.Tick(0f);
            Assert.That(comp.Phases[profile], Is.EqualTo(WGDrugPhase.Heavy));

            Assert.That(solutions.TryGetSolution(mob, chemicals, out var soln, out _), Is.True);
            solutions.RemoveAllSolution(soln!.Value);
            sys.Tick(0f);
            Assert.That(comp.Levels.ContainsKey(profile), Is.False, "Purging the blood ends the high.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TraitTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var sys = server.System<WGIntoxicationSystem>();
            var damageable = server.System<DamageableSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var control = entMan.SpawnEntity("MobHuman", map.GridCoords);
            const string damper = "WGIntoxTestDamper";

            FixedPoint2 Damage(EntityUid uid, string type) => entMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault(type);
            var hit = new DamageSpecifier();
            hit.DamageDict["Heat"] = FixedPoint2.New(10);

            sys.AddLevel(mob, damper, 3f);
            sys.Tick(0.01f);
            Assert.That(entMan.HasComponent<PainNumbnessComponent>(mob), Is.True, "HideDamage should numb the user.");

            damageable.TryChangeDamage(mob, hit);
            damageable.TryChangeDamage(control, hit);
            var comp = entMan.GetComponent<WGIntoxicationComponent>(mob);
            var full = Damage(control, "Heat").Float();
            Assert.Multiple(() =>
            {
                Assert.That(full, Is.GreaterThan(0f));
                Assert.That(Damage(mob, "Heat").Float(), Is.EqualTo(full * 0.7f).Within(0.02f), "Damper holds off 30%.");
                Assert.That(comp.Debt, Is.EqualTo(full * 0.3f * 0.4f).Within(0.02f), "40% of what was held off is owed.");
            });

            var debt = comp.Debt;
            var poisonBefore = Damage(mob, "Poison");
            for (var i = 0; i < 10; i++)
            {
                sys.Tick(1f);
            }

            Assert.Multiple(() =>
            {
                Assert.That(comp.Debt, Is.EqualTo(0f));
                Assert.That((Damage(mob, "Poison") - poisonBefore).Float(), Is.EqualTo(debt).Within(0.05f), "The debt comes back as Poison.");
                Assert.That(entMan.HasComponent<PainNumbnessComponent>(mob), Is.False, "Granted components go with the tier.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BrainCheckTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var sys = server.System<WGIntoxicationSystem>();
            var body = server.System<SharedBodySystem>();
            var protos = server.ResolveDependency<IPrototypeManager>();
            var reagent = protos.Index<ReagentPrototype>("WGIntoxTestReagent");
            var dose = new WGDose { Profile = Profile, PerUnit = 1f };

            void Dose(EntityUid target, float grade)
            {
                var solution = new Solution();
                solution.AddReagent(reagent.ID, FixedPoint2.New(10), grade);
                dose.Effect(new EntityEffectReagentArgs(target, entMan, null, solution, FixedPoint2.New(4), reagent, null, FixedPoint2.New(1)));
            }

            var organic = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(sys.IsNeurowareHost(organic), Is.False);
            Dose(organic, 0.5f);
            Assert.That(entMan.HasComponent<WGIntoxicationComponent>(organic), Is.True, "A metabolised dose starts tracking the body.");
            Assert.That(sys.GetLevel(organic, Profile), Is.EqualTo(0f), "The level comes from the blood, not from the dose.");

            var host = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var brain = body.GetBodyOrganEntityComps<BrainComponent>(host);
            Assert.That(brain, Is.Not.Empty);
            entMan.AddComponent<WGNeurowareHostComponent>(brain[0].Owner);
            Assert.That(sys.IsNeurowareHost(host), Is.True, "A body with a host brain is a host.");
            Dose(host, 1f);
            Assert.That(entMan.HasComponent<WGIntoxicationComponent>(host), Is.False, "Chemical doses don't register on a host.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An antagonist or a reboot that drops a level drops its speed with it.</summary>
    [Test]
    public async Task SpeedRefreshTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var sys = server.System<WGIntoxicationSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var speed = entMan.GetComponent<MovementSpeedModifierComponent>(mob);

            sys.AddLevel(mob, Profile, 4f);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(1.2f).Within(0.001f));
            sys.ClearLevels(mob);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f), "Clearing the levels ends the speed at once.");

            sys.AddLevel(mob, Profile, 4f);
            sys.ReduceFamily(mob, WGDrugFamily.Stimulant, 4f);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f), "So does an antagonist.");

            sys.AddLevel(mob, Profile, 4f);
            entMan.RemoveComponent<WGIntoxicationComponent>(mob);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(1f).Within(0.001f), "And losing the component.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A dependence-only dose in the blood gives no level, and a corpse takes no tier effects.</summary>
    [Test]
    public async Task MedicineAndCorpseTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var sys = server.System<WGIntoxicationSystem>();

            var patient = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var dose = new Solution();
            dose.AddReagent("WGIntoxTestMedicine", FixedPoint2.New(6));
            Assert.That(server.System<BloodstreamSystem>().TryAddToChemicals(patient, dose), Is.True);
            var patientComp = entMan.EnsureComponent<WGIntoxicationComponent>(patient);
            sys.Tick(0f);
            Assert.That(patientComp.Levels, Is.Empty, "A dependence-only dose raises no level.");

            var corpse = entMan.SpawnEntity("MobHuman", map.GridCoords);
            sys.AddLevel(corpse, Profile, 20f);
            var comp = entMan.GetComponent<WGIntoxicationComponent>(corpse);
            Assert.That(sys.GetTier(comp, Profile), Is.EqualTo(1));
            server.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            var before = entMan.GetComponent<DamageableComponent>(corpse).Damage.DamageDict.GetValueOrDefault("Poison");
            sys.Tick(1f);
            var after = entMan.GetComponent<DamageableComponent>(corpse).Damage.DamageDict.GetValueOrDefault("Poison");
            Assert.Multiple(() =>
            {
                Assert.That(after, Is.EqualTo(before), "Tier effects skip a corpse.");
                Assert.That(comp.Levels[Profile], Is.EqualTo(19f).Within(0.001f), "Its level still decays.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An organ that slows a 0.01 u rate below a hundredth a tick still clears the reagent, at that rate.</summary>
    [Test]
    public async Task SlowOrganTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        const string drug = "WGIntoxTestBloodDrug";
        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var organ in server.System<SharedBodySystem>().GetBodyOrganEntityComps<MetabolizerComponent>(mob))
            {
                foreach (var group in organ.Comp1.MetabolismGroups ?? new())
                {
                    if (group.Id == "Narcotic")
                        group.MetabolismRateModifier = FixedPoint2.New(0.2);
                }
            }

            var dose = new Solution();
            dose.AddReagent(drug, FixedPoint2.New(0.1));
            Assert.That(server.System<BloodstreamSystem>().TryAddToChemicals(mob, dose), Is.True);
        });

        await pair.RunSeconds(12);

        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(mob, BloodstreamComponent.DefaultChemicalsSolutionName, out _, out var blood), Is.True);
            var left = blood!.GetTotalPrototypeQuantity(drug);
            Assert.Multiple(() =>
            {
                Assert.That(left, Is.LessThan(FixedPoint2.New(0.1)), "0.002 u a tick adds up to whole hundredths.");
                Assert.That(left, Is.GreaterThanOrEqualTo(FixedPoint2.New(0.07)), "It clears no faster than its rate.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
