#nullable enable
using System;
using System.Linq;
using Content.Client.Inventory;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ClothingLayer;

/// <summary>
/// ClothingLayerSystem stacks clothing the way SS13 does, for native and imported clothing alike and whatever order
/// things are put on in.
/// </summary>
[TestFixture]
public sealed class ClothingLayerTest : InteractionTest
{
    private const string Jumpsuit = "ClothingUniformJumpsuitColorGrey";
    private const string NativeDress = "ClothingUniformJumpskirtJanimaid";
    private const string Skirt = "WFMeridianClothingUnderCostumeCaptainSkirt";
    private const string MaidDress = "WFMeridianClothingUnderCostumeMaid";
    private const string Shoes = "ClothingShoesBootsJack";
    private const string Hardsuit = "ClothingOuterHardsuitEngineering";
    private const string Robe = "WFMeridianClothingSuitHoodedCultrobesEldritchMoon";
    private const string Gaiter = "WFMeridianClothingMaskNeckGaiterSyndicateTacticool";
    private const string GasMask = "WFMeridianClothingMaskGasAtmosAdmin";
    private const string Glasses = "ClothingEyesGlassesSunglasses";
    private const string Labcoat = "ClothingOuterCoatLab";
    private const string Henchmen = "WFMeridianClothingUnderCostumeHenchmen";
    private const string EveningGown = "WFMeridianClothingUnderDressEveninggown";

    [TestCase(Jumpsuit)]
    [TestCase(Skirt)]
    public async Task UniformDrawsOverFeetButUnderHands(string uniform)
    {
        var target = await SpawnTarget("MobHuman");
        await Equip(target, (uniform, "jumpsuit"));

        await Client.WaitAssertion(() =>
        {
            var sprite = CEntMan.GetComponent<SpriteComponent>(ToClient(target));
            var drawn = Indices(target, "jumpsuit");

            int Body(HumanoidVisualLayers part)
            {
                Assert.That(sprite.LayerMapTryGet(part, out var i), Is.True, $"The body has no {part} layer.");
                return i;
            }

            Assert.That(drawn.Min(), Is.GreaterThan(Math.Max(Body(HumanoidVisualLayers.LFoot), Body(HumanoidVisualLayers.RFoot))),
                "The feet drew over the uniform.");
            Assert.That(drawn.Max(), Is.LessThan(Math.Min(Body(HumanoidVisualLayers.LHand), Body(HumanoidVisualLayers.RHand))),
                "The uniform drew over the hands.");
        });
    }

    [TestCase(NativeDress)]
    [TestCase(MaidDress)]
    public async Task DressDrawsOverShoesInEitherOrder(string dress)
    {
        var target = await SpawnTarget("MobHuman");

        await Equip(target, (Shoes, "shoes"), (dress, "jumpsuit"));
        await AssertAbove(target, "jumpsuit", "shoes");

        await Strip(target, "jumpsuit", "shoes");
        await Equip(target, (dress, "jumpsuit"), (Shoes, "shoes"));
        await AssertAbove(target, "jumpsuit", "shoes");
    }

    [Test]
    public async Task TrousersDrawUnderShoes()
    {
        var target = await SpawnTarget("MobHuman");

        await Equip(target, (Jumpsuit, "jumpsuit"), (Shoes, "shoes"));
        await AssertAbove(target, "shoes", "jumpsuit");
    }

    [Test]
    public async Task HardsuitHidesDressUntilTakenOff()
    {
        var target = await SpawnTarget("MobHuman");

        await Equip(target, (MaidDress, "jumpsuit"), (Hardsuit, "outerClothing"));
        await AssertDrawn(target, "jumpsuit", false);

        await Strip(target, "outerClothing");
        await AssertDrawn(target, "jumpsuit", true);

        await Equip(target, (Hardsuit, "outerClothing"));
        await Strip(target, "jumpsuit");
        await Equip(target, (MaidDress, "jumpsuit"));
        await AssertDrawn(target, "jumpsuit", false);
    }

    [Test]
    public async Task GaiterDrawsUnderJumpsuitInEitherOrder()
    {
        var target = await SpawnTarget("MobHuman");

        await Equip(target, (Jumpsuit, "jumpsuit"), (Gaiter, "mask"));
        await AssertAbove(target, "jumpsuit", "mask");

        await Strip(target, "jumpsuit", "mask");
        await Equip(target, (Gaiter, "mask"), (Jumpsuit, "jumpsuit"));
        await AssertAbove(target, "jumpsuit", "mask");
    }

    [Test]
    public async Task RobeHidesShoesUntilTakenOff()
    {
        var target = await SpawnTarget("MobHuman");

        await Equip(target, (Shoes, "shoes"), (Robe, "outerClothing"));
        await AssertDrawn(target, "shoes", false);

        await Strip(target, "outerClothing");
        await AssertDrawn(target, "shoes", true);

        await Equip(target, (Robe, "outerClothing"));
        await Strip(target, "shoes");
        await Equip(target, (Shoes, "shoes"));
        await AssertDrawn(target, "shoes", false);
    }

    [Test]
    public async Task PulledDownMaskStopsHidingGlasses()
    {
        var target = await SpawnTarget("MobHuman");

        var mask = await Equip(target, (Glasses, "eyes"), (GasMask, "mask"));
        await AssertDrawn(target, "eyes", false);

        await Server.WaitAssertion(() => SEntMan.System<MaskSystem>().SetToggled(mask[1], true));
        await RunTicks(10);
        await AssertDrawn(target, "eyes", true);

        await Server.WaitAssertion(() => SEntMan.System<MaskSystem>().SetToggled(mask[1], false));
        await RunTicks(10);
        await AssertDrawn(target, "eyes", false);
    }

    // The lobby dummy and some species put the id layer above the suit, so "under the suit" can't mean "over the id".
    [TestCase("MobHuman")]
    [TestCase("MobHumanDummy")]
    [TestCase("MobChitinid")]
    public async Task UnderSuitUniformDrawsUnderSuit(string mob)
    {
        var target = await SpawnTarget(mob);

        await Equip(target, (Shoes, "shoes"), (EveningGown, "jumpsuit"), (Labcoat, "outerClothing"));
        await AssertAbove(target, "outerClothing", "jumpsuit");
        await AssertAbove(target, "jumpsuit", "shoes");
    }

    [Test]
    public async Task HiddenUniformHidesNothing()
    {
        var target = await SpawnTarget("MobHuman");

        await Equip(target, (Shoes, "shoes"), (Henchmen, "jumpsuit"));
        await AssertDrawn(target, "shoes", false);

        await Equip(target, (Hardsuit, "outerClothing"));
        await AssertDrawn(target, "jumpsuit", false);
        await AssertDrawn(target, "shoes", true);

        await Strip(target, "outerClothing");
        await AssertDrawn(target, "shoes", false);
    }

    [Test]
    public async Task MaskInPocketHidesNothing()
    {
        var target = await SpawnTarget("MobHuman");

        await Equip(target, (Jumpsuit, "jumpsuit"), (Glasses, "eyes"), (GasMask, "pocket1"));
        await AssertDrawn(target, "eyes", true);
    }

    private async Task<EntityUid[]> Equip(NetEntity target, params (string Proto, string Slot)[] items)
    {
        var spawned = new EntityUid[items.Length];
        await Server.WaitAssertion(() =>
        {
            var mob = ToServer(target);
            var inventory = SEntMan.System<InventorySystem>();
            for (var i = 0; i < items.Length; i++)
            {
                var (proto, slot) = items[i];
                spawned[i] = SEntMan.SpawnEntity(proto, SEntMan.GetCoordinates(TargetCoords));
                Assert.That(inventory.TryEquip(mob, spawned[i], slot, force: true), Is.True,
                    $"Could not equip {proto}.");
            }
        });
        await RunTicks(10);
        return spawned;
    }

    private async Task Strip(NetEntity target, params string[] slots)
    {
        await Server.WaitAssertion(() =>
        {
            var mob = ToServer(target);
            var inventory = SEntMan.System<InventorySystem>();
            foreach (var slot in slots)
            {
                Assert.That(inventory.TryUnequip(mob, slot, out var removed, silent: true, force: true), Is.True);
                SEntMan.DeleteEntity(removed);
            }
        });
        await RunTicks(10);
    }

    /// <summary>The wearer's sprite indices of the layers a slot is drawing.</summary>
    private int[] Indices(NetEntity target, string slot)
    {
        var mob = ToClient(target);
        var sprite = CEntMan.GetComponent<SpriteComponent>(mob);
        var slots = CEntMan.GetComponent<InventorySlotsComponent>(mob);
        Assert.That(slots.VisualLayerKeys.TryGetValue(slot, out var keys) && keys.Count > 0, Is.True,
            $"The {slot} slot drew nothing.");
        return keys!.Select(k => sprite.LayerMapTryGet(k, out var i) ? i : -1).ToArray();
    }

    private async Task AssertAbove(NetEntity target, string upper, string lower)
    {
        await Client.WaitAssertion(() =>
            Assert.That(Indices(target, upper).Min(), Is.GreaterThan(Indices(target, lower).Max()),
                $"The {upper} clothing drew under the {lower} clothing."));
    }

    private async Task AssertDrawn(NetEntity target, string slot, bool drawn)
    {
        await Client.WaitAssertion(() =>
        {
            var slots = CEntMan.GetComponent<InventorySlotsComponent>(ToClient(target));
            var count = slots.VisualLayerKeys.TryGetValue(slot, out var keys) ? keys.Count : 0;
            Assert.That(count > 0, Is.EqualTo(drawn),
                drawn ? $"The {slot} clothing wasn't drawn." : $"The {slot} clothing was drawn while covered.");
        });
    }
}
