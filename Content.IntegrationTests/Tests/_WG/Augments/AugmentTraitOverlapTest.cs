using Content.Server._WG.Augments;
using Content.Server.Atmos.Components;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._WG.Augments;
using Content.Shared.Damage.Components;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// The adrenal booster's stamina doesn't buy back Lethargy, whichever came first, and comes back when Lethargy goes.
/// </summary>
[TestFixture]
[TestOf(typeof(WGAdrenalBoosterSystem))]
public sealed class AugmentTraitOverlapTest
{
    [Test]
    public async Task BoosterDoesntBuyBackLethargyTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var humanoids = server.System<SharedHumanoidAppearanceSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var x = 0f;
            EntityUid Spawn()
            {
                var mob = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(x += 2f, 0f)));
                // The test map is airless, and low pressure deals Blunt.
                entities.RemoveComponent<BarotraumaComponent>(mob);
                return mob;
            }

            void Fit(EntityUid mob, ProtoId<WGAugmentPrototype> implant)
            {
                humanoids.LoadProfile(mob, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugmentImplants([implant]));
            }

            StaminaComponent Stamina(EntityUid mob) => entities.GetComponent<StaminaComponent>(mob);

            var plain = Spawn();

            // Lethargy keeps its full cost under a booster, fitted before the trait or after it.
            var lethargic = Spawn();
            entities.EnsureComponent<LethargyComponent>(lethargic);
            var boosterFirst = Spawn();
            Fit(boosterFirst, "WGAugmentAdrenalBooster");
            entities.EnsureComponent<LethargyComponent>(boosterFirst);
            var lethargyFirst = Spawn();
            entities.EnsureComponent<LethargyComponent>(lethargyFirst);
            Fit(lethargyFirst, "WGAugmentAdrenalBooster");

            foreach (var mob in new[] { boosterFirst, lethargyFirst })
            {
                Assert.That(entities.HasComponent<WGAdrenalBoosterComponent>(mob), "The booster wasn't fitted.");
                Assert.That(entities.GetComponent<WGAdrenalBoosterComponent>(mob).StaminaApplied, Is.False);
                Assert.That(Stamina(mob).CritThreshold, Is.EqualTo(Stamina(lethargic).CritThreshold).Within(1e-4),
                    "The booster bought back Lethargy's stamina.");
                Assert.That(Stamina(mob).Decay, Is.EqualTo(Stamina(lethargic).Decay).Within(1e-4));
                Assert.That(Stamina(mob).Cooldown, Is.EqualTo(Stamina(lethargic).Cooldown).Within(1e-4));
            }

            // Lethargy gone, the booster's bonus is back.
            entities.RemoveComponent<LethargyComponent>(boosterFirst);
            var bonus = entities.GetComponent<WGAdrenalBoosterComponent>(boosterFirst).StaminaBonus;
            Assert.That(Stamina(boosterFirst).CritThreshold, Is.EqualTo(Stamina(plain).CritThreshold + bonus).Within(1e-4));

            foreach (var mob in new[] { plain, lethargic, boosterFirst, lethargyFirst })
                entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }
}
