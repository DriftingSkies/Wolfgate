using System.Numerics;
using Content.Shared._WF.SuitTank;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Inventory;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SuitTank;

/// <summary>
/// A tank in a worn suit's 2x2 storage feeds internals and lets go when the suit comes off.
/// </summary>
[TestOf(typeof(WFSuitTankStorageSystem))]
public sealed class WFSuitTankTest
{
    [Test]
    public async Task SuitStorageTankFeedsInternals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var inventory = entities.System<InventorySystem>();
        var storage = entities.System<SharedStorageSystem>();
        var internals = entities.System<SharedInternalsSystem>();

        EntityUid mob = default;
        EntityUid tank = default;

        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f));
            mob = entities.SpawnEntity("MobHuman", coords);
            var suit = entities.SpawnEntity("ClothingOuterHardsuitEVA", coords);
            tank = entities.SpawnEntity("OxygenTankFilled", coords);
            var mask = entities.SpawnEntity("ClothingMaskBreath", coords);
            var jetpack = entities.SpawnEntity("JetpackBlueFilled", coords);
            var crowbar = entities.SpawnEntity("Crowbar", coords);
            var emergencyA = entities.SpawnEntity("EmergencyOxygenTankFilled", coords);
            var emergencyB = entities.SpawnEntity("EmergencyOxygenTankFilled", coords);

            Assert.That(entities.HasComponent<WFSuitTankStorageComponent>(suit));
            Assert.That(inventory.TryEquip(mob, mask, "mask", silent: true, force: true));
            Assert.That(inventory.TryEquip(mob, suit, "outerClothing", silent: true, force: true));

            Assert.That(storage.CanInsert(suit, jetpack, out _), Is.False, "Jetpacks don't go in the suit.");
            Assert.That(storage.CanInsert(suit, crowbar, out _), Is.False, "Only gas tanks go in the suit.");

            // 2x2 holds two small emergency tanks.
            Assert.That(storage.Insert(suit, emergencyA, out _, playSound: false));
            Assert.That(storage.Insert(suit, emergencyB, out _, playSound: false));
            Assert.That(storage.CanInsert(suit, tank, out _), Is.False, "The grid is full.");
            entities.DeleteEntity(emergencyA);
            entities.DeleteEntity(emergencyB);

            Assert.That(storage.Insert(suit, tank, out _, playSound: false));
            Assert.That(inventory.TryGetSlotEntity(mob, "suitstorage", out _), Is.False,
                "The tank leaves suit storage free.");

            Assert.That(internals.FindBestGasTank(mob)?.Owner, Is.EqualTo(tank));
            Assert.That(internals.ToggleInternals(mob, mob, force: true));
        });

        // Past the gas tank system's delayed parent check.
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<InternalsComponent>(mob).GasTankEntity, Is.EqualTo(tank),
                "A tank in the worn suit stays connected.");
            Assert.That(internals.AreInternalsWorking(mob));

            Assert.That(inventory.TryUnequip(mob, "outerClothing", silent: true, force: true));
            Assert.That(entities.GetComponent<InternalsComponent>(mob).GasTankEntity, Is.Null,
                "Taking the suit off disconnects its tank.");
            Assert.That(internals.FindBestGasTank(mob), Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// With two tanks in the suit, internals start on the fuller one and move to the other when it runs out.
    /// </summary>
    [Test]
    public async Task SuitStorageSwitchesTanks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var inventory = entities.System<InventorySystem>();
        var storage = entities.System<SharedStorageSystem>();
        var internals = entities.System<SharedInternalsSystem>();

        EntityUid mob = default;
        EntityUid first = default;
        EntityUid second = default;

        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f));
            mob = entities.SpawnEntity("MobHuman", coords);
            var suit = entities.SpawnEntity("ClothingOuterHardsuitEVA", coords);
            var mask = entities.SpawnEntity("ClothingMaskBreath", coords);
            first = entities.SpawnEntity("EmergencyOxygenTankFilled", coords);
            second = entities.SpawnEntity("EmergencyOxygenTankFilled", coords);

            Assert.That(inventory.TryEquip(mob, mask, "mask", silent: true, force: true));
            Assert.That(inventory.TryEquip(mob, suit, "outerClothing", silent: true, force: true));
            Assert.That(storage.Insert(suit, first, out _, playSound: false));
            Assert.That(storage.Insert(suit, second, out _, playSound: false));

            // The second tank goes in fuller, so it should be picked over storage order.
            entities.GetComponent<GasTankComponent>(first).Air.Remove(0.01f);
            Assert.That(internals.FindBestGasTank(mob)?.Owner, Is.EqualTo(second));
            Assert.That(internals.ToggleInternals(mob, mob, force: true));

            entities.GetComponent<GasTankComponent>(second).Air.Clear();
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<InternalsComponent>(mob).GasTankEntity, Is.EqualTo(first),
                "Internals move to the other suit tank once the connected one runs out.");
            Assert.That(entities.GetComponent<GasTankComponent>(second).User, Is.Null);
            Assert.That(internals.AreInternalsWorking(mob));
        });

        await pair.CleanReturnAsync();
    }
}
