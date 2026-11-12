#nullable enable
using System.Linq;
using Content.Client.Inventory;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.NeckSuits;
using Content.Server.Temperature.Components;
using Content.Shared._WF.NeckSuits;
using Content.Shared.Armor;
using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.NeckSuits;

/// <summary>A suit adjusted for neck use loses its protection, draws over the worn suit and adjusts back intact.</summary>
[TestFixture]
public sealed class WFNeckSuitTest : InteractionTest
{
    private const string Coat = "ClothingOuterWinterCoat";

    [Test]
    public async Task NeckModeIsCosmeticAndLayersOverTheSuit()
    {
        var target = await SpawnTarget("MobHuman");
        var mob = ToServer(target);
        EntityUid coat = default;

        await Server.WaitAssertion(() =>
        {
            coat = SEntMan.SpawnEntity(Coat, SEntMan.GetCoordinates(TargetCoords));
            var clothing = SEntMan.GetComponent<ClothingComponent>(coat);
            var system = SEntMan.System<WFNeckSuitSystem>();
            Assert.That(system.CanAdjust((coat, clothing)), Is.True);

            system.SetNeckMode((coat, clothing), true);
            Assert.That(clothing.Slots, Is.EqualTo(SlotFlags.NECK));
            Assert.That(SEntMan.HasComponent<WFNeckSuitComponent>(coat), Is.True);
            Assert.That(SEntMan.HasComponent<ArmorComponent>(coat), Is.False, "Neck mode kept the armor.");
            Assert.That(SEntMan.HasComponent<TemperatureProtectionComponent>(coat), Is.False,
                "Neck mode kept the temperature protection.");

            var inventory = SEntMan.System<InventorySystem>();
            Assert.That(inventory.CanEquip(mob, coat, "outerClothing", out _), Is.False,
                "A neck-mode suit went in the suit slot.");
            Assert.That(inventory.TryEquip(mob, coat, "neck", force: true), Is.True);
        });
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var wearer = ToClient(target);
            var sprite = CEntMan.GetComponent<SpriteComponent>(wearer);
            var slots = CEntMan.GetComponent<InventorySlotsComponent>(wearer);
            var sprites = CEntMan.System<SpriteSystem>();

            Assert.That(slots.VisualLayerKeys.TryGetValue("neck", out var keys) && keys.Count > 0, Is.True,
                "The neck-mode suit wasn't drawn.");
            var key = keys!.First();
            Assert.That(sprites.LayerMapTryGet((wearer, sprite), key, out var index, false), Is.True);
            Assert.That(sprite[index].RsiState.Name, Does.Contain("equipped-OUTERCLOTHING"),
                "The neck-mode suit didn't draw its outer-suit sprite.");

            Assert.That(sprites.LayerMapTryGet((wearer, sprite), "outerClothing", out var suit, false), Is.True);
            Assert.That(sprites.LayerMapTryGet((wearer, sprite), HumanoidVisualLayers.Hair, out var hair, false), Is.True);
            Assert.That(index, Is.GreaterThan(suit), "The neck-mode suit drew under the suit slot.");
            Assert.That(index, Is.LessThan(hair), "The neck-mode suit drew over the hair.");
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<InventorySystem>().TryUnequip(mob, "neck", force: true), Is.True);
            var clothing = SEntMan.GetComponent<ClothingComponent>(coat);
            SEntMan.System<WFNeckSuitSystem>().SetNeckMode((coat, clothing), false);
            Assert.That(clothing.Slots & SlotFlags.OUTERCLOTHING, Is.Not.EqualTo(SlotFlags.NONE));
            Assert.That(SEntMan.HasComponent<WFNeckSuitComponent>(coat), Is.False);
            Assert.That(SEntMan.HasComponent<ArmorComponent>(coat), Is.True, "Adjusting back didn't restore the armor.");
            Assert.That(SEntMan.HasComponent<TemperatureProtectionComponent>(coat), Is.True,
                "Adjusting back didn't restore the temperature protection.");
        });
    }

    [Test]
    public async Task SpaceSuitsAreFunctionalOnly()
    {
        await Server.WaitAssertion(() =>
        {
            var suit = SEntMan.SpawnEntity("ClothingOuterHardsuitEngineering", SEntMan.GetCoordinates(TargetCoords));
            var clothing = SEntMan.GetComponent<ClothingComponent>(suit);
            Assert.That(SEntMan.System<WFNeckSuitSystem>().CanAdjust((suit, clothing)), Is.False);
        });
    }
}
