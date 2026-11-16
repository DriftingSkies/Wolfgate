using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SuitTank;

/// <summary>Sealing and retracting a MODsuit helmet through the native module system restores head layers.</summary>
[TestFixture]
public sealed class ModsuitHiddenLayersTest
{
    private static readonly string[] Suits = ["ClothingModsuitStandardPowerCell", "ClothingModsuitParamedicPowerCell"];

    [Test]
    [TestCaseSource(nameof(Suits))]
    public async Task RetractedHelmetRestoresLayers(string suitId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var inventory = entMan.System<InventorySystem>();

        EntityUid mob = default;
        EntityUid suit = default;
        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            mob = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity(suitId, coords);
            Assert.That(inventory.TryEquip(mob, suit, "back", silent: true, force: true));
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            modSuit.ToggleAllParts((suit, comp), mob);
        });

        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.TryToggleActivation((suit, comp), mob));
        });

        await server.WaitRunTicks(360);

        await server.WaitAssertion(() =>
        {
            // Some helmets cover the head while sealed, others leave it visible; the retraction below is what this
            // test guards.
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.TryToggleActivation((suit, comp), mob));
        });

        await server.WaitRunTicks(360);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.State, Is.EqualTo(ModSuitState.Inactive));
            modSuit.ToggleAllParts((suit, comp), mob); // retracting every part should start
        });

        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(mob);
            Assert.That(inventory.TryGetSlotEntity(mob, "head", out var helmet), Is.False, "the helmet should be retracted");
            Assert.That(humanoid.HiddenLayers, Is.Empty, "retracting the helmet should show every layer again");
        });

        await pair.CleanReturnAsync();
    }
}
