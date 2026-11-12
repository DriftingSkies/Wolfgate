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
using Content.Shared.StepTrigger.Components;
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

            Assert.That(system.SetNeckMode((coat, clothing), true), Is.True);
            Assert.That(clothing.Slots, Is.EqualTo(SlotFlags.NECK));
            Assert.That(SEntMan.HasComponent<WFNeckSuitComponent>(coat), Is.True);
            Assert.That(SEntMan.HasComponent<ArmorComponent>(coat), Is.False, "Neck mode kept the armor.");
            Assert.That(SEntMan.HasComponent<TemperatureProtectionComponent>(coat), Is.False,
                "Neck mode kept the temperature protection.");

            var inventory = SEntMan.System<InventorySystem>();
            Assert.That(inventory.CanEquip(mob, coat, "outerClothing", out _), Is.False,
                "A neck-mode suit went in the suit slot.");
            Assert.That(inventory.TryEquip(mob, coat, "neck", force: true), Is.True);

            Assert.That(system.SetNeckMode((coat, clothing), false), Is.False,
                "A worn neck-mode suit was adjusted back in its slot.");
            Assert.That(SEntMan.HasComponent<ArmorComponent>(coat), Is.False);
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

            // The client predicts equips from these slots.
            Assert.That(CEntMan.GetComponent<ClothingComponent>(ToClient(coat)).Slots, Is.EqualTo(SlotFlags.NECK),
                "The client doesn't know the suit goes in a neck slot.");
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<InventorySystem>().TryUnequip(mob, "neck", force: true), Is.True);
            var clothing = SEntMan.GetComponent<ClothingComponent>(coat);
            Assert.That(SEntMan.System<WFNeckSuitSystem>().SetNeckMode((coat, clothing), false), Is.True);
            Assert.That(clothing.Slots & SlotFlags.OUTERCLOTHING, Is.Not.EqualTo(SlotFlags.NONE));
            Assert.That(SEntMan.HasComponent<WFNeckSuitComponent>(coat), Is.False);
            Assert.That(SEntMan.HasComponent<ArmorComponent>(coat), Is.True, "Adjusting back didn't restore the armor.");
            Assert.That(SEntMan.HasComponent<TemperatureProtectionComponent>(coat), Is.True,
                "Adjusting back didn't restore the temperature protection.");
        });
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.GetComponent<ClothingComponent>(ToClient(coat)).Slots & SlotFlags.OUTERCLOTHING,
                Is.Not.EqualTo(SlotFlags.NONE), "The client doesn't know the suit is back in the suit slot.");
        });
    }

    /// <summary>Protection beyond armor goes too: fire resistance and step-trigger immunity.</summary>
    [TestCase("ClothingOuterVestWebElite", typeof(FireProtectionComponent))]
    [TestCase("ClothingOuterArmorHeavy", typeof(ProtectedFromStepTriggersComponent))]
    public async Task NeckModeStripsProtection(string suitId, Type protection)
    {
        await Server.WaitAssertion(() =>
        {
            var suit = SEntMan.SpawnEntity(suitId, SEntMan.GetCoordinates(TargetCoords));
            var clothing = SEntMan.GetComponent<ClothingComponent>(suit);
            var system = SEntMan.System<WFNeckSuitSystem>();

            Assert.That(system.SetNeckMode((suit, clothing), true), Is.True);
            Assert.That(SEntMan.HasComponent(suit, protection), Is.False, $"Neck mode kept {protection.Name}.");
            Assert.That(system.SetNeckMode((suit, clothing), false), Is.True);
            Assert.That(SEntMan.HasComponent(suit, protection), Is.True, $"Adjusting back lost {protection.Name}.");
        });
    }

    [TestCase("ClothingOuterHardsuitEngineering")]
    [TestCase("WFRMCGhillie")]
    public async Task SpecialSuitsAreFunctionalOnly(string suitId)
    {
        await Server.WaitAssertion(() =>
        {
            var suit = SEntMan.SpawnEntity(suitId, SEntMan.GetCoordinates(TargetCoords));
            var clothing = SEntMan.GetComponent<ClothingComponent>(suit);
            Assert.That(SEntMan.System<WFNeckSuitSystem>().CanAdjust((suit, clothing)), Is.False);
        });
    }

    /// <summary>Two neck suits stack in slot order, however often the lower one is redrawn.</summary>
    [Test]
    public async Task TwoNeckSuitsKeepSlotOrder()
    {
        var target = await SpawnTarget("MobHuman");
        var mob = ToServer(target);

        await Server.WaitAssertion(() =>
        {
            var inventory = SEntMan.System<InventorySystem>();
            var system = SEntMan.System<WFNeckSuitSystem>();
            foreach (var slot in new[] { "neck2", "neck" })
            {
                var coat = SEntMan.SpawnEntity(Coat, SEntMan.GetCoordinates(TargetCoords));
                system.SetNeckMode((coat, SEntMan.GetComponent<ClothingComponent>(coat)), true);
                Assert.That(inventory.TryEquip(mob, coat, slot, force: true), Is.True);
            }
        });
        await RunTicks(5);

        await Client.WaitAssertion(() =>
        {
            var wearer = ToClient(target);
            var inventory = CEntMan.GetComponent<InventoryComponent>(wearer);
            var clothing = CEntMan.System<Content.Client.Clothing.ClientClothingSystem>();

            // Redraw everything twice; neck must stay under neck2 either way.
            for (var i = 0; i < 2; i++)
            {
                clothing.InitClothing(wearer, inventory);
                Assert.That(TopIndex(wearer, "neck"), Is.LessThan(TopIndex(wearer, "neck2")),
                    "The neck slot's suit drew over neck2's.");
            }
        });
    }

    private int TopIndex(EntityUid wearer, string slot)
    {
        var sprite = CEntMan.GetComponent<SpriteComponent>(wearer);
        var slots = CEntMan.GetComponent<InventorySlotsComponent>(wearer);
        var sprites = CEntMan.System<SpriteSystem>();
        Assert.That(slots.VisualLayerKeys.TryGetValue(slot, out var keys) && keys.Count > 0, Is.True,
            $"Nothing drawn in {slot}.");
        return keys!.Max(k => sprites.LayerMapTryGet((wearer, sprite), k, out var i, false) ? i : -1);
    }
}
