#nullable enable
using Content.Server._WF.Shipyard;
using Content.Shared._Mono.Economy.Components;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Shipyard;

/// <summary>
/// A resold ship gets back what its machines only set up at map init, which a loaded grid never runs.
/// </summary>
[TestFixture]
[TestOf(typeof(UsedShipReinitSystem))]
public sealed class UsedShipReinitTest
{
    [Test]
    public async Task CashSlotComesBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var itemSlots = entMan.System<ItemSlotsSystem>();
        var reinit = entMan.System<UsedShipReinitSystem>();

        await server.WaitAssertion(() =>
        {
            var vendor = entMan.SpawnEntity("VendingMachineCola", map.GridCoords);
            var slotName = entMan.GetComponent<CreditReceiverComponent>(vendor).CashSlotName;

            // The slot list is not saved with the grid, so a resold machine loads without it.
            Assert.That(itemSlots.TryGetSlot(vendor, slotName, out var slot), Is.True);
            itemSlots.RemoveItemSlot(vendor, slot!);
            Assert.That(itemSlots.TryGetSlot(vendor, slotName, out _), Is.False);

            reinit.ReinitLoadedShip(map.Grid.Owner);

            Assert.That(itemSlots.TryGetSlot(vendor, slotName, out _), Is.True,
                "A resold ship's vending machine came back without its cash slot.");
        });

        await pair.CleanReturnAsync();
    }
}
