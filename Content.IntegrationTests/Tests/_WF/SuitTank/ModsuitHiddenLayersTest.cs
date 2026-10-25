using Content.Shared._Goobstation.Clothing.Components;
using Content.Shared._Goobstation.Clothing.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SuitTank;

/// <summary>Sealing and unsealing a modsuit leaves no head layers hidden once the helmet is retracted.</summary>
[TestFixture]
public sealed class ModsuitHiddenLayersTest
{
    private static readonly string[] Suits = ["ClothingModsuitStandartPowerCell", "ClothingModsuitParamedicPowerCell"];

    [Test]
    [TestCaseSource(nameof(Suits))]
    public async Task RetractedHelmetRestoresLayers(string suitId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var sealing = entMan.System<SharedSealableClothingSystem>();

        EntityUid mob = default;
        EntityUid suit = default;
        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            var inventory = entMan.System<InventorySystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            mob = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity(suitId, coords);
            Assert.That(inventory.TryEquip(mob, suit, "back", silent: true, force: true));

            entMan.EventBus.RaiseLocalEvent(suit, new ToggleClothingEvent { Performer = mob });
            Assert.That(sealing.TryStartSealToggleProcess((suit, entMan.GetComponent<SealableClothingControlComponent>(suit)), mob));
        });

        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<SealableClothingControlComponent>(suit).IsCurrentlySealed, Is.True);
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(mob);
            Assert.That(humanoid.HiddenLayers, Does.ContainKey(HumanoidVisualLayers.Hair), "a sealed helmet should hide the hair");
            Assert.That(sealing.TryStartSealToggleProcess((suit, entMan.GetComponent<SealableClothingControlComponent>(suit)), mob));
        });

        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<SealableClothingControlComponent>(suit).IsCurrentlySealed, Is.False);
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(mob);
            var helmet = entMan.System<InventorySystem>().TryGetSlotEntity(mob, "head", out var head) ? head.Value : default;
            Assert.That(humanoid.HiddenLayers.ContainsKey(HumanoidVisualLayers.Hair),
                Is.EqualTo(entMan.HasComponent<HideLayerClothingComponent>(helmet)),
                "an unsealed helmet should hide the hair only if it always does");
            if (suitId == "ClothingModsuitParamedicPowerCell")
                Assert.That(entMan.HasComponent<HideLayerClothingComponent>(helmet), "unsealing must not strip what the helmet always has");

            entMan.EventBus.RaiseLocalEvent(suit, new ToggleClothingEvent { Performer = mob });
            Assert.That(humanoid.HiddenLayers, Is.Empty, "retracting the helmet should show every layer again");
        });

        await pair.CleanReturnAsync();
    }
}
