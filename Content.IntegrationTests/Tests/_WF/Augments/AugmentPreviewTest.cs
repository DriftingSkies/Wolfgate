using System.Collections.Generic;
using Content.Shared._WF.Augments;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>The creator's preview doll draws the picked augments, hides a missing arm and drops them when unpicked.</summary>
[TestFixture]
[TestOf(typeof(WFAugmentRules))]
public sealed class AugmentPreviewTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wfAugment
  id: WFTestPreviewMissingLeftArm
  name: wf-augment-cyber-arm
  description: wf-augment-cyber-arm-desc
  slot: LeftArm
  kind: Missing
";

    [Test]
    public async Task PreviewDollTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entities = client.ResolveDependency<IEntityManager>();
        var humanoids = entities.System<SharedHumanoidAppearanceSystem>();

        var plain = HumanoidCharacterProfile.DefaultWithSpecies("Human");
        var augmented = plain.WithAugments(new Dictionary<WFAugmentSlot, WFAugmentChoice>
        {
            [WFAugmentSlot.RightArm] = new("WFAugmentCyberRightArm", "WFAugmentStyleEEZengHu"),
            [WFAugmentSlot.LeftArm] = new("WFTestPreviewMissingLeftArm", null),
        });

        await client.WaitAssertion(() =>
        {
            var doll = entities.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
            var sprite = entities.GetComponent<SpriteComponent>(doll);

            humanoids.LoadProfile(doll, augmented);
            var humanoid = entities.GetComponent<HumanoidAppearanceComponent>(doll);
            Assert.That(humanoid.BaseLayers[HumanoidVisualLayers.RArm].ID, Is.EqualTo("WFMobAugmentEEZengHuRArm"),
                "The doll draws the natural arm.");
            Assert.That(humanoid.BaseLayers[HumanoidVisualLayers.RHand].ID, Is.EqualTo("WFMobAugmentEEZengHuRHand"));
            Assert.That(sprite[sprite.LayerMapGet(HumanoidVisualLayers.LArm)].Visible, Is.False, "The missing arm shows.");

            humanoids.LoadProfile(doll, plain);
            Assert.That(humanoid.BaseLayers[HumanoidVisualLayers.RArm].ID, Does.Not.Contain("Augment"),
                "The augment stayed on the doll after it was unpicked.");
            Assert.That(sprite[sprite.LayerMapGet(HumanoidVisualLayers.LArm)].Visible, Is.True);
            entities.DeleteEntity(doll);
        });

        await pair.CleanReturnAsync();
    }
}
