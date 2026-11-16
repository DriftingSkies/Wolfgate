using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.WolfChem.Narcotics;
using Content.Server.Body.Components;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.WolfChem.Narcotics;

/// <summary>
/// Doses build dependence and tolerance, the meter fades while clean, withdrawal starts after going without and only
/// a high ends it, and the habit traits start the round dependent with a first fix that suits the body.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDependenceSystem))]
public sealed class DependenceTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wfDrugProfile
  id: WFDependenceTestProfile
  family: Opioid
  decay: 1
  dependenceGain: 0.1
  tiers:
  - min: 2
";

    private const string Profile = "WFDependenceTestProfile";

    [Test]
    public async Task GainDecayWithdrawalTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var intox = server.System<WFIntoxicationSystem>();
            var dependence = server.System<WFDependenceSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);

            intox.AddLevel(mob, Profile, 4f);
            var dep = entMan.GetComponent<WFDependenceComponent>(mob);
            Assert.Multiple(() =>
            {
                Assert.That(dep.Meter[WFDrugFamily.Opioid], Is.EqualTo(0.4f).Within(0.001f), "Gain is dependenceGain per level.");
                Assert.That(dep.Tolerance[WFDrugFamily.Opioid], Is.EqualTo(0.2f).Within(0.001f));
            });

            var patient = entMan.SpawnEntity("MobHuman", map.GridCoords);
            intox.RegisterDose(patient, Profile, 4f, false);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<WFIntoxicationComponent>(patient), Is.False, "A dose that gives no level tracks no high.");
                Assert.That(entMan.GetComponent<WFDependenceComponent>(patient).Meter[WFDrugFamily.Opioid], Is.EqualTo(0.4f).Within(0.001f));
            });

            intox.Tick(1f);
            Assert.That(intox.GetLevel(mob, Profile), Is.EqualTo(2.8f).Within(0.001f), "Tolerance speeds up decay.");

            for (var i = 0; i < 5; i++)
            {
                intox.Tick(1f);
            }

            dependence.Tick(60f);
            Assert.That(dep.Meter[WFDrugFamily.Opioid], Is.EqualTo(0.38f).Within(0.001f), "The meter loses 0.02 a minute clean.");
            Assert.That(dep.Stage.GetValueOrDefault(WFDrugFamily.Opioid), Is.EqualTo(0), "0.38 is below withdrawal.");

            dep.Meter[WFDrugFamily.Opioid] = 0.9f;
            dependence.Tick(WFDependenceSystem.WithdrawalDelay - 61f);
            Assert.That(dep.Stage.GetValueOrDefault(WFDrugFamily.Opioid), Is.EqualTo(0), "Withdrawal waits five minutes.");

            dep.Meter[WFDrugFamily.Opioid] = 0.9f;
            dependence.Tick(2f);
            Assert.That(dep.Stage[WFDrugFamily.Opioid], Is.EqualTo(3), "A meter of 0.9 is the top stage.");

            intox.AddLevel(mob, Profile, 0.5f);
            dependence.Tick(1f);
            Assert.That(dep.Stage[WFDrugFamily.Opioid], Is.EqualTo(3), "A taste below the first tier doesn't end withdrawal.");

            intox.AddLevel(mob, Profile, 4f);
            dependence.Tick(1f);
            Assert.Multiple(() =>
            {
                Assert.That(dep.Stage[WFDrugFamily.Opioid], Is.EqualTo(0), "Reaching the first tier does.");
                Assert.That(dep.Satisfied, Does.Contain(WFDrugFamily.Opioid), "A dependent body that is high is satisfied.");
            });
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
            var dependence = server.System<WFDependenceSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WFDependentStimulantComponent>(mob);
            entMan.AddComponent<WFDependentNicotineComponent>(mob);

            var dep = entMan.GetComponent<WFDependenceComponent>(mob);
            Assert.Multiple(() =>
            {
                Assert.That(dep.Meter[WFDrugFamily.Stimulant], Is.EqualTo(0.8f).Within(0.001f));
                Assert.That(dep.Meter[WFDrugFamily.Nicotine], Is.EqualTo(0.8f).Within(0.001f), "Traits stack.");
            });

            dependence.Tick(100000f);
            Assert.That(dep.Meter[WFDrugFamily.Stimulant], Is.EqualTo(0.55f).Within(0.001f), "A trait keeps the meter at 0.55.");
            Assert.That(WFDependenceSystem.StageFor(WFDrugFamily.Stimulant, dep.Meter[WFDrugFamily.Stimulant]), Is.EqualTo(1), "A clean trait eases to cravings.");
            Assert.Multiple(() =>
            {
                Assert.That(WFDependenceSystem.StageFor(WFDrugFamily.Stimulant, 0.9f), Is.EqualTo(3));
                Assert.That(WFDependenceSystem.StageFor(WFDrugFamily.Nicotine, 0.9f), Is.EqualTo(1), "Nicotine stops at cravings.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TraitGearTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var body = server.System<SharedBodySystem>();
            var hands = server.System<SharedHandsSystem>();

            List<string> Held(EntityUid mob)
            {
                return hands.EnumerateHeld(mob).Select(item => entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID).ToList();
            }

            var traits = new (string Trait, string Gear, string HostGear, WFDrugFamily Family)[]
            {
                ("WFDependentStimulant", "WFBaggieCocaineCut", "WFNeurowareStickWhitelineCopy", WFDrugFamily.Stimulant),
                ("WFDependentOpioid", "WFBaggieHeroinCut", "WFNeurowareStickNullLoopCopy", WFDrugFamily.Opioid),
                ("WFDependentEmpathogen", "WFVialHaloCut", "WFNeurowareStickHaloCopy", WFDrugFamily.Empathogen),
                ("WFDependentCannabinoid", "Joint", "WFNeurowareStickIdleCopy", WFDrugFamily.Cannabinoid),
            };

            foreach (var (trait, gear, hostGear, family) in traits)
            {
                var proto = protoMan.Index<TraitPrototype>(trait);

                var organic = entMan.SpawnEntity("MobHuman", map.GridCoords);
                entMan.AddComponents(organic, proto.Components, false);

                var host = entMan.SpawnEntity("MobHuman", map.GridCoords);
                entMan.AddComponent<WFNeurowareHostComponent>(body.GetBodyOrganEntityComps<BrainComponent>(host)[0].Owner);
                entMan.AddComponents(host, proto.Components, false);

                Assert.Multiple(() =>
                {
                    Assert.That(Held(organic), Does.Contain(gear), $"{trait} hands an organic its drug.");
                    Assert.That(Held(organic), Does.Not.Contain(hostGear));
                    Assert.That(Held(host), Does.Contain(hostGear), $"{trait} hands a host its program.");
                    Assert.That(Held(host), Does.Not.Contain(gear));
                    Assert.That(entMan.GetComponent<WFDependenceComponent>(host).Meter[family], Is.EqualTo(0.8f).Within(0.001f));
                    Assert.That(proto.BiotypeBlacklist, Is.Empty, $"{trait} is open to synthetics.");
                });
            }

            Assert.That(WFDependenceSystem.StageFor(WFDrugFamily.Cannabinoid, 0.9f), Is.EqualTo(1), "Cannabis stops at cravings.");
        });

        await pair.CleanReturnAsync();
    }
}
