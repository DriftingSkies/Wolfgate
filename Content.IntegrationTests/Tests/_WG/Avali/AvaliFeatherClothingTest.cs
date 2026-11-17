#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WG.Avali.Feathers;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Avali;

/// <summary>Feather overlays preserve each item's tint across head and ear slots, including rapid swaps.</summary>
[TestFixture]
public sealed class AvaliFeatherClothingTest : InteractionTest
{
    [Test]
    public async Task TwoFeathersKeepTheirTintsAcrossSlotsAndRapidSwaps()
    {
        var target = await SpawnTarget("MobAvali");
        var mob = ToServer(target);
        var headColor = new Color(0.82f, 0.24f, 0.16f);
        var earColor = new Color(0.16f, 0.48f, 0.86f);
        EntityUid headFeather = default;
        EntityUid earFeather = default;

        await Server.WaitAssertion(() =>
        {
            headFeather = SEntMan.SpawnEntity("AvaliFeather", SEntMan.GetCoordinates(TargetCoords));
            earFeather = SEntMan.SpawnEntity("AvaliFeather", SEntMan.GetCoordinates(TargetCoords));
            var appearance = SEntMan.System<SharedAppearanceSystem>();
            appearance.SetData(headFeather, FeatherVisuals.FeatherColor, headColor);
            appearance.SetData(earFeather, FeatherVisuals.FeatherColor, earColor);

            var inventory = SEntMan.System<InventorySystem>();
            Assert.That(inventory.TryEquip(mob, headFeather, "head", force: true), Is.True);
            Assert.That(inventory.TryEquip(mob, earFeather, "ears", force: true), Is.True);
        });
        await RunTicks(10);

        async Task AssertLayerColors(Color expectedHead, Color expectedEars)
        {
            await Client.WaitAssertion(() =>
            {
                var spriteUid = ToClient(target);
                var sprites = CEntMan.System<SpriteSystem>();
                var sprite = CEntMan.GetComponent<SpriteComponent>(spriteUid);
                Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), "head-feather", out var headLayer, false), Is.True,
                    "The head feather visual layer was missing.");
                Assert.That(sprites.LayerMapTryGet((spriteUid, sprite), "ears-feather", out var earsLayer, false), Is.True,
                    "The ear feather visual layer was missing.");
                Assert.That(sprite[headLayer].Color, Is.EqualTo(expectedHead), "The head feather tint changed.");
                Assert.That(sprite[earsLayer].Color, Is.EqualTo(expectedEars), "The ear feather tint changed.");
            });
        }

        await AssertLayerColors(headColor, earColor);

        // Remove and re-equip both items in the opposite slots within one server tick to cover stale overlays.
        await Server.WaitAssertion(() =>
        {
            var inventory = SEntMan.System<InventorySystem>();
            Assert.That(inventory.TryUnequip(mob, "head", silent: true, force: true), Is.True);
            Assert.That(inventory.TryUnequip(mob, "ears", silent: true, force: true), Is.True);
            Assert.That(inventory.TryEquip(mob, headFeather, "ears", force: true), Is.True);
            Assert.That(inventory.TryEquip(mob, earFeather, "head", force: true), Is.True);
        });
        await RunTicks(10);

        await AssertLayerColors(earColor, headColor);
    }
}
