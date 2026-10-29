using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Augments;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>
/// Every roundstart species takes cybernetic arms and legs: the parts are swapped where its body has them, without
/// harm, and each limb draws the style fitted to that species' own limb.
/// </summary>
[TestFixture]
[TestOf(typeof(WFAugmentRules))]
public sealed class AugmentSpeciesTest
{
    private static readonly HumanoidVisualLayers[] Limbs =
    {
        HumanoidVisualLayers.LArm, HumanoidVisualLayers.RArm, HumanoidVisualLayers.LHand, HumanoidVisualLayers.RHand,
        HumanoidVisualLayers.LLeg, HumanoidVisualLayers.RLeg, HumanoidVisualLayers.LFoot, HumanoidVisualLayers.RFoot,
    };

    [Test]
    public async Task EverySpeciesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var entities = server.ResolveDependency<IEntityManager>();
        var humanoids = entities.System<SharedHumanoidAppearanceSystem>();
        var bodies = entities.System<SharedBodySystem>();
        const string style = "WFAugmentStyleE3N";

        await server.WaitAssertion(() =>
        {
            foreach (var species in proto.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart).OrderBy(s => s.ID))
            {
                var choices = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
                foreach (var slot in new[] { WFAugmentSlot.LeftArm, WFAugmentSlot.RightArm, WFAugmentSlot.LeftLeg, WFAugmentSlot.RightLeg })
                {
                    if (WFAugmentRules.AugmentsFor(species.ID, slot, proto, factory).FirstOrDefault(a => a.Kind == WFAugmentKind.Limb) is { } limb)
                        choices[slot] = new WFAugmentChoice(limb.ID, style);
                }
                if (choices.Count == 0)
                    continue;

                var mob = entities.SpawnEntity(species.Prototype, map.GridCoords);
                var before = entities.GetComponent<DamageableComponent>(mob).TotalDamage;
                var profile = HumanoidCharacterProfile.DefaultWithSpecies(species.ID).WithAugments(choices);
                Assert.DoesNotThrow(() => humanoids.LoadProfile(mob, profile), species.ID);

                var humanoid = entities.GetComponent<HumanoidAppearanceComponent>(mob);
                var stylePrototype = proto.Index<WFAugmentStylePrototype>(style);
                var digitigrade = WFAugmentRules.Digitigrade(profile, proto);
                foreach (var layer in Limbs)
                {
                    var under = WFAugmentRules.Underlying(humanoid, layer, proto);
                    var expected = WFAugmentRules.Sprite(stylePrototype, layer, digitigrade, under, proto);
                    Assert.That(humanoid.CustomBaseLayers.TryGetValue(layer, out var drawn) ? drawn.Id : null, Is.EqualTo(expected),
                        $"{species.ID} {layer} over {under}");
                }

                var augmented = bodies.GetBodyChildren(mob)
                    .Count(p => entities.GetComponent<MetaDataComponent>(p.Id).EntityPrototype?.ID.StartsWith("WFAugment") == true);
                Assert.That(augmented, Is.EqualTo(choices.Count), $"{species.ID} didn't get every cybernetic limb.");
                Assert.That(entities.GetComponent<DamageableComponent>(mob).TotalDamage, Is.LessThanOrEqualTo(before),
                    $"{species.ID} was hurt by the swap.");
                TestContext.Out.WriteLine($"{species.ID}: {choices.Count} limbs, " +
                    $"{Limbs.Count(l => humanoid.CustomBaseLayers.TryGetValue(l, out var d) && stylePrototype.Fitted.ContainsValue(d.Id!.Value))} fitted layers");
                entities.DeleteEntity(mob);
            }
        });

        await pair.CleanReturnAsync();
    }
}
