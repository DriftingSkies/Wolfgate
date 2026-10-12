#nullable enable
using System.Collections.Generic;
using Content.Server.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Avali;

/// <summary>Ammonia quenches an Avali's thirst and is safe for it to breathe; a human gets neither.</summary>
[TestFixture]
public sealed class AvaliAmmoniaTest
{
    private const string Ammonia = "Ammonia";
    private const float StartThirst = 100f;

    [Test]
    public async Task AmmoniaQuenchesAvaliThirstTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var thirst = entMan.System<ThirstSystem>();
        var blood = entMan.System<BloodstreamSystem>();

        EntityUid avali = default, human = default;
        await server.WaitAssertion(() =>
        {
            avali = entMan.SpawnEntity("MobAvali", map.GridCoords);
            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            foreach (var mob in new[] { avali, human })
            {
                thirst.SetThirst(mob, entMan.GetComponent<ThirstComponent>(mob), StartThirst);
                Assert.That(blood.TryAddToChemicals(mob, new Solution(Ammonia, 10)), Is.True);
            }
        });
        await pair.RunSeconds(15);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<ThirstComponent>(avali).CurrentThirst, Is.GreaterThan(StartThirst + 50f),
                    "Ammonia did not quench the Avali's thirst.");
                Assert.That(entMan.GetComponent<ThirstComponent>(human).CurrentThirst, Is.LessThanOrEqualTo(StartThirst),
                    "Ammonia quenched a human's thirst.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AvaliBreathesAmmoniaTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var body = entMan.System<SharedBodySystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();

        EntityUid avali = default, human = default;
        await server.WaitAssertion(() =>
        {
            avali = entMan.SpawnEntity("MobAvali", map.GridCoords);
            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        // Breathing swaps the lungs' contents, so keep them topped up.
        for (var i = 0; i < 20; i++)
        {
            await server.WaitAssertion(() =>
            {
                foreach (var mob in new[] { avali, human })
                {
                    var lungs = body.GetBodyOrganEntityComps<LungComponent>((mob, null));
                    Assert.That(lungs, Is.Not.Empty);
                    foreach (var lung in lungs)
                    {
                        Assert.That(solutions.ResolveSolution(lung.Owner, lung.Comp1.SolutionName, ref lung.Comp1.Solution));
                        solutions.TryAddReagent(lung.Comp1.Solution!.Value, Ammonia, 50);
                    }
                }
            });
            await pair.RunTicksSync(15);
        }

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Damage(entMan, avali, "Poison"), Is.EqualTo(FixedPoint2.Zero),
                    "Breathing ammonia poisoned the Avali.");
                Assert.That(Damage(entMan, human, "Poison"), Is.GreaterThan(FixedPoint2.Zero),
                    "Breathing ammonia did not poison the human.");
            });
        });

        await pair.CleanReturnAsync();
    }

    private static FixedPoint2 Damage(IEntityManager entMan, EntityUid mob, string type)
    {
        return entMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.GetValueOrDefault(type);
    }
}
