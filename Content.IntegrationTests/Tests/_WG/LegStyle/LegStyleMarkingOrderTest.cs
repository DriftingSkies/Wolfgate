using System.Collections.Generic;
using Content.Client._WG.LegStyle;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.LegStyle;

/// <summary>
/// Leg and foot markings of both sides draw above both of the pair's base layers, or the far limb's bare skin hides
/// them in a side view.
/// </summary>
[TestFixture]
[TestOf(typeof(LegStyleSystem))]
public sealed class LegStyleMarkingOrderTest
{
    [Test]
    public async Task LimbMarkingsDrawAbovePairTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entities = client.ResolveDependency<IEntityManager>();
        var humanoids = entities.System<SharedHumanoidAppearanceSystem>();

        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
        profile = profile.WithCharacterAppearance(profile.Appearance.WithMarkings(new List<Marking>
        {
            new("LegProstheticLeft", 1),
            new("LegProstheticRight", 1),
            new("FootProstheticLeft", 1),
            new("FootProstheticRight", 1),
        }));

        await client.WaitAssertion(() =>
        {
            var doll = entities.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
            var sprites = entities.System<SpriteSystem>();
            var sprite = entities.GetComponent<SpriteComponent>(doll);
            humanoids.LoadProfile(doll, profile);

            int Index(string key)
            {
                Assert.That(sprites.LayerMapTryGet((doll, sprite), key, out var index, false), Is.True, $"{key} isn't drawn.");
                return index;
            }

            int Base(HumanoidVisualLayers part) => sprites.LayerMapGet((doll, sprite), part);

            var topLeg = Math.Max(Base(HumanoidVisualLayers.LLeg), Base(HumanoidVisualLayers.RLeg));
            var topFoot = Math.Max(Base(HumanoidVisualLayers.LFoot), Base(HumanoidVisualLayers.RFoot));

            Assert.Multiple(() =>
            {
                Assert.That(Index("LegProstheticLeft-leg_l_prosthetic"), Is.GreaterThan(topLeg));
                Assert.That(Index("LegProstheticRight-leg_r_prosthetic"), Is.GreaterThan(topLeg));
                Assert.That(Index("FootProstheticLeft-foot_l_prosthetic"), Is.GreaterThan(topFoot));
                Assert.That(Index("FootProstheticRight-foot_r_prosthetic"), Is.GreaterThan(topFoot));

                // Still under clothing and the feet.
                Assert.That(Index("LegProstheticRight-leg_r_prosthetic"), Is.LessThan(Index("jumpsuit")));
                Assert.That(Index("FootProstheticLeft-foot_l_prosthetic"), Is.LessThan(Index("shoes")));
            });

            entities.DeleteEntity(doll);
        });

        await pair.CleanReturnAsync();
    }
}
