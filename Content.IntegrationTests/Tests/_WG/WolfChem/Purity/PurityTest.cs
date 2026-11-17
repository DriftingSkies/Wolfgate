using System.Collections.Generic;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Server._WG.WolfChem.Purity;
using Content.Shared._WG.WolfChem.Purity;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.EntityEffects.EffectConditions;
using Content.Shared.Body.Organ;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Purity;

/// <summary>
/// Reactions grade their product, metabolism works at a reagent's grade and poisons for the rest, thresholds count
/// effective units, and grades survive serialisation.
/// </summary>
[TestFixture]
[TestOf(typeof(Solution))]
public sealed class PurityTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGPurityTestBeaker
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 100

- type: reagent
  id: WGPurityTestA
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: WGPurityTestB
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: WGPurityTestProduct
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: WGPurityTestC
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reagent
  id: WGPurityTestTuned
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing

- type: reaction
  id: WGPurityTestPassReaction
  reactants:
    WGPurityTestA:
      amount: 1
    WGPurityTestB:
      amount: 1
  products:
    WGPurityTestProduct: 2

- type: reaction
  id: WGPurityTestTunedReaction
  reactants:
    WGPurityTestC:
      amount: 1
  products:
    WGPurityTestTuned: 1

- type: wgReactionPurity
  id: WGPurityTestTunedReaction
  optimalTemp: 300
  tolerance: 100
  floor: 0.4

- type: reagent
  id: WGPurityTestHeal
  name: reagent-name-nothing
  desc: reagent-desc-nothing
  physicalDesc: reagent-physical-desc-nothing
  metabolisms:
    Medicine:
      metabolismRate: 1
      effects:
      - !type:WGClearIntoxication
        family: Stimulant
        amount: 2

- type: wgDrugProfile
  id: WGPurityTestProfile
  family: Stimulant
  decay: 0
  tiers:
  - min: 1000
";

    [Test]
    public async Task ReactionTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();

            Solution Mix(params Solution[] inputs)
            {
                var beaker = server.EntMan.SpawnEntity("WGPurityTestBeaker", map.GridCoords);
                solutions.TryGetSolution(beaker, "beaker", out var ent, out var solution);
                foreach (var input in inputs)
                {
                    solutions.TryAddSolution(ent!.Value, input);
                }

                return solution!;
            }

            Solution Graded(string reagent, float grade, float temperature = 300f)
            {
                var solution = new Solution { Temperature = temperature };
                solution.AddReagent(reagent, FixedPoint2.New(10), grade);
                return solution;
            }

            var passed = Mix(Graded("WGPurityTestA", 0.5f), Graded("WGPurityTestB", 1f));
            Assert.That(passed.GetTotalPrototypeQuantity("WGPurityTestProduct"), Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(passed.GetPurity("WGPurityTestProduct"), Is.EqualTo(0.5f).Within(0.01f),
                "A standard-grade reactant should not raise the product's grade.");

            var both = Mix(Graded("WGPurityTestA", 0.5f), Graded("WGPurityTestB", 0.9f));
            Assert.That(both.GetPurity("WGPurityTestProduct"), Is.EqualTo(0.7f).Within(0.01f),
                "Graded reactants should pass their mean grade through.");

            var optimal = Mix(Graded("WGPurityTestC", 1f, 300f));
            Assert.That(optimal.GetPurity("WGPurityTestTuned"), Is.EqualTo(1f).Within(0.01f));

            var hot = Mix(Graded("WGPurityTestC", 1f, 350f));
            Assert.That(hot.GetPurity("WGPurityTestTuned"), Is.EqualTo(0.7f).Within(0.01f),
                "Off-optimum temperature should lower the product grade.");

            var floor = Mix(Graded("WGPurityTestC", 0.5f, 500f));
            Assert.That(floor.GetPurity("WGPurityTestTuned"), Is.EqualTo(0.2f).Within(0.01f));

            var reactions = server.System<ChemicalReactionSystem>();
            reactions.WGOptimumShift["WGPurityTestTunedReaction"] = 50f;
            var shifted = Mix(Graded("WGPurityTestC", 1f, 350f));
            reactions.WGOptimumShift.Remove("WGPurityTestTunedReaction");
            Assert.That(shifted.GetPurity("WGPurityTestTuned"), Is.EqualTo(1f).Within(0.01f),
                "A round's shift moves the optimum.");

            server.System<WGPurityDriftSystem>().Roll();
            var purities = server.ProtoMan.EnumeratePrototypes<WGReactionPurityPrototype>();
            Assert.Multiple(() =>
            {
                Assert.That(reactions.WGOptimumShift, Is.Not.Empty, "The street reactions drift.");
                foreach (var purity in purities)
                {
                    var shift = reactions.WGOptimumShift.GetValueOrDefault(purity.ID);
                    Assert.That(MathF.Abs(shift), Is.LessThanOrEqualTo(purity.Drift), purity.ID);
                    if (purity.OptimalTemp is not { } optimal || !server.ProtoMan.TryIndex<ReactionPrototype>(purity.ID, out var reaction))
                        continue;

                    Assert.That(optimal - purity.Drift, Is.GreaterThanOrEqualTo(reaction.MinimumTemperature), $"{purity.ID} can drift below its reaction's range.");
                    Assert.That(optimal + purity.Drift, Is.LessThanOrEqualTo(reaction.MaximumTemperature), $"{purity.ID} can drift above its reaction's range.");
                }
            });
            reactions.WGOptimumShift.Clear();

            var cut = Mix(Graded("WGCocaine", 0.8f), Graded("Flour", 1f));
            Assert.Multiple(() =>
            {
                Assert.That(cut.GetTotalPrototypeQuantity("WGCocaine"), Is.EqualTo(FixedPoint2.New(20)));
                Assert.That(cut.GetTotalPrototypeQuantity("Flour"), Is.EqualTo(FixedPoint2.Zero));
                Assert.That(cut.GetPurity("WGCocaine"), Is.EqualTo(0.4f).Within(0.01f),
                    "A filler should cut the grade by the volume it adds.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MetabolismTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid standard = default;
        EntityUid graded = default;
        const string profile = "WGPurityTestProfile";

        // Wolfmed clears small amounts of Poison within seconds, so make the impurity damage large enough to read.
        await server.WaitPost(() => server.CfgMan.SetCVar(WGPurityCVars.ImpurityDamage, 20f));

        await server.WaitPost(() =>
        {
            var bloodstream = server.System<BloodstreamSystem>();
            var intox = server.System<WGIntoxicationSystem>();

            standard = entMan.SpawnEntity("MobHuman", map.GridCoords);
            graded = entMan.SpawnEntity("MobHuman", map.GridCoords);
            intox.AddLevel(standard, profile, 100f);
            intox.AddLevel(graded, profile, 100f);

            var good = new Solution();
            good.AddReagent("WGPurityTestHeal", FixedPoint2.New(2));
            var cut = new Solution();
            cut.AddReagent("WGPurityTestHeal", FixedPoint2.New(2), 0.5f);
            Assert.That(bloodstream.TryAddToChemicals(standard, good), Is.True);
            Assert.That(bloodstream.TryAddToChemicals(graded, cut), Is.True);
        });

        await pair.RunSeconds(12);

        await server.WaitAssertion(() =>
        {
            var intox = server.System<WGIntoxicationSystem>();
            var standardEffect = 100f - intox.GetLevel(standard, profile);
            var gradedEffect = 100f - intox.GetLevel(graded, profile);
            var unitsMetabolised = standardEffect / 2f;
            var standardPoison = entMan.GetComponent<DamageableComponent>(standard).Damage.DamageDict.GetValueOrDefault("Poison").Float();
            var gradedPoison = entMan.GetComponent<DamageableComponent>(graded).Damage.DamageDict.GetValueOrDefault("Poison").Float();

            Assert.Multiple(() =>
            {
                Assert.That(standardEffect, Is.GreaterThan(0f), "The standard dose should take effect.");
                Assert.That(gradedEffect, Is.EqualTo(standardEffect / 2f).Within(0.05f), "A 50% dose should work at half strength.");
                Assert.That(gradedPoison - standardPoison, Is.GreaterThan(unitsMetabolised * 0.5f * 20f * 0.5f),
                    "The impure half of each unit should deal Poison.");
                Assert.That(standardPoison, Is.EqualTo(0f), "Standard grade deals no impurity damage.");
            });
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(WGPurityCVars.ImpurityDamage, 1f));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChemMasterTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();
            var master = entMan.SpawnEntity("ChemMaster", map.GridCoords);
            var beaker = entMan.SpawnEntity("Beaker", map.GridCoords);
            Assert.That(solutions.TryGetFitsInDispenser(beaker, out var beakerSoln, out var beakerSolution), Is.True);
            beakerSolution!.AddReagent("WGPurityTestA", FixedPoint2.New(20), 0.6f);
            Assert.That(server.System<ItemSlotsSystem>().TryInsert(master, SharedChemMaster.InputSlotName, beaker, null), Is.True);
            Assert.That(solutions.TryGetSolution(master, SharedChemMaster.BufferSolutionName, out _, out var buffer), Is.True);

            void Press(bool fromBuffer)
            {
                var message = new ChemMasterReagentAmountButtonMessage(new ReagentId("WGPurityTestA", null),
                    ChemMasterReagentAmount.U10, fromBuffer);
                entMan.EventBus.RaiseLocalEvent(master, (object) message);
            }

            Press(false);
            Assert.Multiple(() =>
            {
                Assert.That(buffer!.GetTotalPrototypeQuantity("WGPurityTestA"), Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(buffer.GetPurity("WGPurityTestA"), Is.EqualTo(0.6f).Within(0.001f),
                    "A reagent keeps its grade in the buffer.");
            });

            Press(false);
            Assert.That(beakerSolution.Purity, Is.Null);
            Press(true);
            Assert.Multiple(() =>
            {
                Assert.That(beakerSolution.GetTotalPrototypeQuantity("WGPurityTestA"), Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(beakerSolution.GetPurity("WGPurityTestA"), Is.EqualTo(0.6f).Within(0.001f),
                    "A reagent keeps its grade on the way back out.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DigestionTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var swallowed = new Solution();
            swallowed.AddReagent("WGPurityTestA", FixedPoint2.New(30), 0.4f);

            var fed = false;
            var query = entMan.EntityQueryEnumerator<StomachComponent, OrganComponent>();
            while (query.MoveNext(out var uid, out var stomach, out var organ))
            {
                if (organ.Body == mob)
                    fed = server.System<StomachSystem>().TryTransferSolution(uid, swallowed, stomach);
            }

            Assert.That(fed, Is.True);
        });

        // Digestion takes 20 s, then the body flushes a reagent with no metabolism at 1 u a second.
        await pair.RunSeconds(25);

        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(mob, BloodstreamComponent.DefaultChemicalsSolutionName, out _, out var blood), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(blood!.GetTotalPrototypeQuantity("WGPurityTestA"), Is.GreaterThan(FixedPoint2.New(10)));
                Assert.That(blood.GetPurity("WGPurityTestA"), Is.EqualTo(0.4f).Within(0.001f),
                    "A swallowed reagent keeps its grade in the blood.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThresholdTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var protos = server.ResolveDependency<IPrototypeManager>();
            var reagent = protos.Index<ReagentPrototype>("WGPurityTestHeal");
            var target = server.EntMan.SpawnEntity(null, map.GridCoords);
            var threshold = new ReagentThreshold { Min = FixedPoint2.New(5) };

            bool Trips(float grade)
            {
                var solution = new Solution();
                solution.AddReagent(reagent.ID, FixedPoint2.New(8), grade);
                var args = new EntityEffectReagentArgs(target, server.EntMan, null, solution, FixedPoint2.New(1), reagent, null, FixedPoint2.New(1));
                return threshold.Condition(args);
            }

            Assert.Multiple(() =>
            {
                Assert.That(Trips(1f), Is.True, "8 standard units pass a 5 u threshold.");
                Assert.That(Trips(0.5f), Is.False, "8 u at 50% is 4 effective units.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SerializationTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var serialization = server.ResolveDependency<ISerializationManager>();

            var solution = new Solution();
            solution.AddReagent("WGPurityTestA", FixedPoint2.New(10), 0.3f);
            solution.AddReagent("WGPurityTestB", FixedPoint2.New(10));
            var read = serialization.Read<Solution>(serialization.WriteValue(solution, notNullableOverride: true), notNullableOverride: true);
            Assert.That(read.GetPurity("WGPurityTestA"), Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(read.Purity!.ContainsKey("WGPurityTestB"), Is.False);

            var plain = new Solution();
            plain.AddReagent("WGPurityTestB", FixedPoint2.New(10));
            var plainRead = serialization.Read<Solution>(serialization.WriteValue(plain, notNullableOverride: true), notNullableOverride: true);
            Assert.That(plainRead.Purity, Is.Null);
        });

        await pair.CleanReturnAsync();
    }
}
