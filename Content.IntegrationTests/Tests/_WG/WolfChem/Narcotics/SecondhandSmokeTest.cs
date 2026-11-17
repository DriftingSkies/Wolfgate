using Content.Server._WG.WolfChem.Narcotics;
using Content.Server.Body.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Smoking;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// Smoking leaves a share of the drug in the air; bystanders breathing smoky air get a mix weighted towards the nearer
/// source, and never more than was left.
/// </summary>
[TestFixture]
[TestOf(typeof(WGSecondhandSmokeSystem))]
public sealed class SecondhandSmokeTest
{
    [Test]
    public async Task MixedSmokeTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var smoke = server.System<WGSecondhandSmokeSystem>();
        EntityUid cigarette = default;

        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();

            FixedPoint2 InBlood(EntityUid mob, string reagent)
            {
                solutions.TryGetSolution(mob, BloodstreamComponent.DefaultChemicalsSolutionName, out _, out var chemicals);
                return chemicals!.GetTotalPrototypeQuantity(reagent);
            }

            var near = entMan.SpawnEntity(null, map.GridCoords);
            var far = entMan.SpawnEntity(null, new EntityCoordinates(map.Grid, 6.5f, 0.5f));
            var nicotine = new Solution("Nicotine", FixedPoint2.New(10));
            smoke.Exhale(near, nicotine);
            smoke.Exhale(far, new Solution("THC", FixedPoint2.New(10)));
            Assert.That(nicotine.Volume, Is.EqualTo(FixedPoint2.New(8.5)), "The smoker keeps 85%.");
            Assert.That(smoke.Reservoir(near), Is.EqualTo(FixedPoint2.New(1.5)), "15% stays in the air.");

            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            smoke.Breathe(mob, 0f, 2f);
            Assert.That(smoke.Reservoir(near), Is.EqualTo(FixedPoint2.New(1.5)), "Clear air carries nothing.");

            smoke.Breathe(mob, 5f, 2f);
            Assert.That(InBlood(mob, "Nicotine"), Is.GreaterThan(InBlood(mob, "THC")), "The nearer source dominates.");
            Assert.That(InBlood(mob, "THC"), Is.GreaterThan(FixedPoint2.Zero), "The farther one still mixes in.");

            for (var i = 0; i < 100; i++)
            {
                smoke.Breathe(mob, 5f, 2f);
            }

            Assert.That(smoke.Reservoir(near) + smoke.Reservoir(far), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(InBlood(mob, "Nicotine"), Is.EqualTo(FixedPoint2.New(1.5)), "Only what was left is breathed.");
            Assert.That(InBlood(mob, "THC"), Is.EqualTo(FixedPoint2.New(1.5)));

            cigarette = entMan.SpawnEntity("Cigarette", map.GridCoords);
            server.System<SmokingSystem>().SetSmokableState(cigarette, SmokableState.Lit);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            Assert.That(smoke.Reservoir(cigarette), Is.GreaterThan(FixedPoint2.Zero), "A lit cigarette smokes into the air.");
        });

        await pair.CleanReturnAsync();
    }
}
