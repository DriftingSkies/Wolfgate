using System.Linq;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Shared._WG.WolfChem.Narcotics;
using Content.Shared._WG.Wolfmed.Reagents;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// Heroin's sedation reaches Wolfmed's airloss threshold only around the overdose tier, and grade and tolerance
/// blunt it as they blunt the high.
/// </summary>
[TestFixture]
[TestOf(typeof(WGSedation))]
public sealed class HeroinSedationTest
{
    [Test]
    public async Task SedationFollowsTierTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var heroin = server.ProtoMan.Index<ReagentPrototype>("WGHeroin");
            var sedation = heroin.Metabolisms!["Narcotic"].Effects.OfType<WGSedation>().Single();
            var overdose = server.ProtoMan.Index<WGDrugProfilePrototype>("WGHeroin").Tiers[^1].Min;
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);

            float Target(float units, float grade = 1f)
            {
                var blood = new Solution();
                blood.AddReagent(heroin.ID, FixedPoint2.New(units));
                blood.SetPurity(heroin.ID, grade);
                sedation.Effect(new EntityEffectReagentArgs(mob, entMan, null, blood, FixedPoint2.New(0.01), heroin,
                    null, FixedPoint2.New(1)));
                return entMan.GetComponent<WolfmedPainReliefComponent>(mob).Doses[heroin.ID].SedationTarget;
            }

            var threshold = entMan.EnsureComponent<WolfmedPainReliefComponent>(mob).SedationAirlossThreshold;
            Assert.That(Target(5f), Is.LessThan(threshold), "Two lines don't stop the breathing.");
            Assert.That(Target(10f), Is.LessThan(threshold), "Four lines are a heavy nod, not an overdose.");
            Assert.That(Target(overdose), Is.GreaterThan(threshold), "At the overdose tier the breathing fails.");
            Assert.That(Target(overdose, 0.5f), Is.LessThan(threshold), "Half grade sedates half as much.");

            entMan.EnsureComponent<WGDependenceComponent>(mob).Tolerance[WGDrugFamily.Opioid] = 1f;
            Assert.That(Target(overdose), Is.LessThan(threshold), "Full tolerance doubles what it takes.");
            Assert.That(Target(overdose * 2f), Is.GreaterThan(threshold));
        });

        await pair.CleanReturnAsync();
    }
}
