using Content.Shared._WG.Stealth;
using Content.Shared.Inventory;
using Content.Shared.Overlays;
using Content.Shared.Stealth.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Stealth;

/// <summary>Checks the stealth helmet's toggle switches the wearer's stealth and keeps it off across a re-equip.</summary>
[TestFixture]
public sealed class WGStealthToggleTest
{
    private const string Helmet = "ClothingHeadHelmetHardsuitCybersunStealth";

    [Test]
    public async Task ToggleSwitchesWearerStealth()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();

        EntityUid human = default;
        EntityUid helmet = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            human = entities.SpawnEntity("MobHuman", coords);
            helmet = entities.SpawnEntity(Helmet, coords);

            Assert.That(entities.HasComponent<NightVisionComponent>(helmet), Is.True, "The helmet carries the CV-53 night vision.");
            Assert.That(entities.HasComponent<Content.Shared._White.Overlays.ThermalVisionComponent>(helmet), Is.True, "The helmet carries thermals.");
            Assert.That(inventory.TryEquip(human, helmet, "head", silent: true, force: true), Is.True);
            Assert.That(entities.GetComponent<StealthComponent>(human).Enabled, Is.True, "Stealth starts on.");
            Assert.That(entities.HasComponent<WGStealthGlowComponent>(human), Is.True, "The visor glows through the field.");
        });

        // Let the field cloak the wearer, then switch it off: it fades back in before turning off.
        await server.WaitRunTicks(200);
        await server.WaitAssertion(() =>
        {
            entities.EventBus.RaiseLocalEvent(helmet, new WGToggleStealthEvent { Performer = human });
            Assert.That(entities.GetComponent<StealthComponent>(human).Enabled, Is.True, "Switching off fades instead of snapping.");
            Assert.That(entities.HasComponent<WGStealthFadingOutComponent>(human), Is.True);
            Assert.That(entities.HasComponent<WGStealthSuppressedComponent>(human), Is.True);
        });

        await server.WaitRunTicks(150);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<StealthComponent>(human).Enabled, Is.False, "The fade finished and the field is off.");
            Assert.That(entities.HasComponent<WGStealthFadingOutComponent>(human), Is.False);

            Assert.That(inventory.TryUnequip(human, "head", silent: true, force: true), Is.True);
            Assert.That(entities.HasComponent<WGStealthGlowComponent>(human), Is.False);
            Assert.That(entities.HasComponent<WGStealthSuppressedComponent>(human), Is.False, "Taking the helmet off clears the marker.");
            Assert.That(inventory.TryEquip(human, helmet, "head", silent: true, force: true), Is.True);
            Assert.That(entities.GetComponent<StealthComponent>(human).Enabled, Is.False, "The helmet remembers it was switched off.");

            entities.EventBus.RaiseLocalEvent(helmet, new WGToggleStealthEvent { Performer = human });
            Assert.That(entities.GetComponent<StealthComponent>(human).Enabled, Is.True);
            Assert.That(entities.HasComponent<WGStealthSuppressedComponent>(human), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnequipWithStealthOn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();

        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            human = entities.SpawnEntity("MobHuman", coords);
            var helmet = entities.SpawnEntity(Helmet, coords);
            Assert.That(inventory.TryEquip(human, helmet, "head", silent: true, force: true), Is.True);
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<StealthComponent>(human).Enabled, Is.True);
            Assert.That(inventory.TryUnequip(human, "head", silent: true, force: true), Is.True);
            Assert.That(entities.HasComponent<StealthComponent>(human), Is.False, "Taking the helmet off ends the stealth.");
        });

        await server.WaitRunTicks(30);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RetractHelmetWithStealthOn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();

        EntityUid human = default;
        EntityUid suit = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            human = entities.SpawnEntity("MobHuman", coords);
            suit = entities.SpawnEntity("ClothingOuterHardsuitCybersunStealth", coords);
            Assert.That(inventory.TryEquip(human, suit, "outerClothing", silent: true, force: true), Is.True);
            entities.EventBus.RaiseLocalEvent(suit, new Content.Shared.Clothing.EntitySystems.ToggleClothingEvent { Performer = human });
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<StealthComponent>(human).Enabled, Is.True, "Deploying the helmet starts stealth.");
            entities.EventBus.RaiseLocalEvent(suit, new Content.Shared.Clothing.EntitySystems.ToggleClothingEvent { Performer = human });
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<StealthComponent>(human), Is.False, "Retracting the helmet ends the stealth.");
        });

        await pair.CleanReturnAsync();
    }
}
