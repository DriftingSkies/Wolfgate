using System.Linq;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Server.Body.Systems;
using Content.Shared._WG.WolfChem.Narcotics;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// The analyzer's conditions readout names the reagent behind a high and a held effect, how long it takes to clear,
/// the family's antidotes, and dependence with its withdrawal.
/// </summary>
[TestFixture]
[TestOf(typeof(WGAnalyzerConditionsSystem))]
public sealed class AnalyzerConditionsTest
{
    [Test]
    public async Task ConditionsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var conditions = server.System<WGAnalyzerConditionsSystem>();
            var intoxication = server.System<WGIntoxicationSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);

            var dose = new Solution();
            dose.AddReagent("THC", FixedPoint2.New(5));
            dose.AddReagent("Ethanol", FixedPoint2.New(10));
            Assert.That(server.System<BloodstreamSystem>().TryAddToChemicals(mob, dose), Is.True);

            var intox = entMan.EnsureComponent<WGIntoxicationComponent>(mob);
            intoxication.Tick(0f);
            intox.Levels["WGCocaine"] = 1f;
            server.System<WGAlcoholSystem>().HoldDrunkenness(mob, 60f, false);

            var dep = entMan.EnsureComponent<WGDependenceComponent>(mob);
            dep.Meter[WGDrugFamily.Opioid] = 0.8f;
            dep.Tolerance[WGDrugFamily.Opioid] = 0.3f;
            dep.Stage[WGDrugFamily.Opioid] = 2;

            var result = conditions.Build(mob);
            var cannabis = result.Drugs.Single(drug => drug.Family == WGDrugFamily.Cannabinoid);
            var cocaine = result.Drugs.Single(drug => drug.Family == WGDrugFamily.Stimulant);
            var drunk = result.Held.Single(held => held.Key == "Drunk");
            var habit = result.Habits.Single(habit => habit.Family == WGDrugFamily.Opioid);

            Assert.Multiple(() =>
            {
                Assert.That(cannabis.Causes.Select(cause => cause.Reagent), Is.EquivalentTo(new[] { "THC" }),
                    "THC in the blood is behind the cannabis level.");
                Assert.That(cannabis.Causes[0].Quantity, Is.EqualTo(FixedPoint2.New(5)));
                Assert.That(cannabis.Causes[0].ClearSeconds, Is.GreaterThan(0f), "The heart clears THC.");
                Assert.That(cocaine.Causes, Is.Empty, "A level with nothing in the blood has no cause.");
                Assert.That(cocaine.Antidotes, Does.Contain("Haloperidol"), "Haloperidol brings stimulants down.");
                Assert.That(cannabis.Antidotes, Does.Contain("Psicodine"), "Psicodine calms a cannabis high.");
                Assert.That(drunk.Causes.Select(cause => cause.Reagent), Does.Contain("Ethanol"),
                    "Ethanol in the blood holds the drunkenness.");
                Assert.That(drunk.SecondsLeft, Is.EqualTo(60f).Within(1f));
                Assert.That(habit.Stage, Is.EqualTo(2));
                Assert.That(habit.Tolerance, Is.EqualTo(0.3f).Within(0.001f));
                Assert.That(habit.CleanIn, Is.EqualTo((0.8f - WGDependenceSystem.WithdrawalMeter) / (WGDependenceSystem.DecayPerSecond * WGDependenceSystem.DependentDecay)).Within(1f));
            });
        });

        await pair.CleanReturnAsync();
    }
}
