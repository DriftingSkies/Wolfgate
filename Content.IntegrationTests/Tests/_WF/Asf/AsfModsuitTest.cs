using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared._Mono.ArmorPlate;
using Content.Shared._NF.Clothing.Components;
using Content.Shared._Mono.PersonalShield;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Asf;

/// <summary>Checks the ASF field modsuits take plates and cover wings, and that the Aegis shield only comes up on a sealed suit.</summary>
[TestFixture]
public sealed class AsfModsuitTest
{
    private static readonly string[] Chestplates = ["WFAsfClothingModsuitChestplateField", "WFAsfClothingModsuitChestplateAegis"];

    [Test]
    public async Task ChestplatesCoverWingsAndKeepTheirSeal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in Chestplates)
            {
                var proto = prototypes.Index<EntityPrototype>(id);
                // Monolith#4845 took plate holders off every hardsuit and modsuit, ASF included.
                Assert.That(proto.TryComp<ArmorPlateHolderComponent>(out _, factory), Is.False, $"{id} follows upstream and takes no armour plates.");
                Assert.That(proto.TryComp<HarpyHideWingsComponent>(out _, factory), Is.True, $"{id} should cover wings like a hardsuit.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AegisShieldNeedsASealedSuit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var maps = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();
        var toggle = entities.System<ItemToggleSystem>();
        var modSuit = entities.System<ModSuitSystem>();

        EntityUid human = default;
        EntityUid suit = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            human = entities.SpawnEntity("MobHuman", coords);
            suit = entities.SpawnEntity("WFAsfClothingModsuitAegisPowerCell", coords);

            Assert.That(entities.GetComponent<PersonalShieldComponent>(suit).Shield.RequiredSlot, Is.EqualTo(SlotFlags.BACK));
            Assert.That(inventory.TryEquip(human, suit, "back", silent: true, force: true), Is.True);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.PartEntities, Has.Count.EqualTo(4));
            Assert.That(toggle.TryActivate(suit, human, predicted: false), Is.False, "The shield must stay down on an unsealed suit.");
            modSuit.ToggleAllParts((suit, comp), human);
        });

        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.TryToggleActivation((suit, comp), human));
        });

        await server.WaitRunTicks(360);

        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.State, Is.EqualTo(ModSuitState.Active));
            Assert.That(toggle.TryActivate(suit, human, predicted: false), Is.True, "The shield should switch on once the suit is sealed.");
        });

        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<PersonalShieldComponent>(suit).IsUp, Is.True, "The Aegis shield should be up after spinning up.");
            var comp = entities.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.TryToggleActivation((suit, comp), human));
        });

        await server.WaitRunTicks(360);

        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.State, Is.EqualTo(ModSuitState.Inactive));
            Assert.That(toggle.IsActivated(suit), Is.False, "Unsealing the suit should drop the shield.");
        });

        await pair.CleanReturnAsync();
    }
}
