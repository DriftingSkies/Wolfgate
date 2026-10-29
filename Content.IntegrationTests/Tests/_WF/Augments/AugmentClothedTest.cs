using System.Collections.Generic;
using System.Linq;
using Content.Client.Inventory;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.Augments;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>A clothed Vulpkanin with cybernetic arms draws them on the client the way flesh arms are drawn.</summary>
[TestOf(typeof(WFAugmentRules))]
public sealed class AugmentClothedTest : InteractionTest
{
    protected override string PlayerPrototype => "MobVulpkanin";

    [Test]
    public async Task ClothedArmsTest()
    {
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Vulpkanin").WithAugments(new Dictionary<WFAugmentSlot, WFAugmentChoice>
        {
            [WFAugmentSlot.RightArm] = new("WFAugmentCyberRightArm", "WFAugmentStyleXion2"),
            [WFAugmentSlot.LeftArm] = new("WFAugmentCyberLeftArm", "WFAugmentStyleXion2"),
        });

        // As a spawn does it: the loadout goes on first, then the profile loads and swaps the arms.
        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            var inventory = SEntMan.System<InventorySystem>();
            foreach (var (slot, item) in new[] { ("jumpsuit", "ClothingUniformJumpsuitColorGrey"), ("gloves", "ClothingHandsGlovesColorBlack"), ("outerClothing", "ClothingOuterVestHazard") })
                Assert.That(inventory.TryEquip(SPlayer, SEntMan.SpawnEntity(item, coords), slot, true, true), Is.True, slot);
            SEntMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(SPlayer, profile);
        });
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var sprite = CEntMan.GetComponent<SpriteComponent>(CPlayer);
            // Vulpkanin draw their hands before their feet; the uniform must still stay under the hands.
            var suitTop = CEntMan.GetComponent<InventorySlotsComponent>(CPlayer).VisualLayerKeys["jumpsuit"]
                .Max(key => sprite.LayerMapGet(key));
            foreach (var hand in new[] { HumanoidVisualLayers.LHand, HumanoidVisualLayers.RHand })
                Assert.That(sprite.LayerMapGet(hand), Is.GreaterThan(suitTop), $"The jumpsuit draws over the {hand}.");

            foreach (var layer in new[] { HumanoidVisualLayers.LArm, HumanoidVisualLayers.RArm, HumanoidVisualLayers.LHand, HumanoidVisualLayers.RHand })
            {
                var spriteLayer = (SpriteComponent.Layer) sprite[sprite.LayerMapGet(layer)];
                Assert.That(spriteLayer.Visible, $"{layer} is hidden under the clothes.");
                Assert.That(spriteLayer.ActualRsi?.Path.ToString(), Does.Contain("_WF/Augments/xion2.rsi"), $"{layer} draws flesh.");
            }
        });
    }
}
