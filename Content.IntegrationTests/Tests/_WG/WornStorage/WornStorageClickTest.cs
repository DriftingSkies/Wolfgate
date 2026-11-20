#nullable enable
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Client.UserInterface.Systems.Inventory;
using Content.Client.UserInterface.Systems.Storage;
using Content.Client.UserInterface.Systems.Storage.Controls;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WG.WornStorage;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Timing;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.WornStorage;

/// <summary>
/// With <see cref="WornStorageCVars.ClickOpens"/> on, clicking a worn backpack opens it, a held item still goes inside,
/// and dropping it on an empty hand takes it off into that hand. Off, a click takes it off as before. The same goes
/// for storage inside an open storage window.
/// </summary>
[TestOf(typeof(InventoryUIController))]
public sealed class WornStorageClickTest : InteractionTest
{
    private const string Slot = "back";

    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task ClickOpensAndDragTakesOff()
    {
        var inventory = SEntMan.System<InventorySystem>();
        var hands = SEntMan.System<SharedHandsSystem>();
        var ui = SEntMan.System<SharedUserInterfaceSystem>();
        var cfg = Client.ResolveDependency<IConfigurationManager>();
        EntityUid backpack = default;

        await Server.WaitPost(() =>
        {
            backpack = SEntMan.SpawnEntity("ClothingBackpack", SEntMan.GetCoordinates(PlayerCoords));
            Assert.That(inventory.TryEquip(SPlayer, backpack, Slot, silent: true, force: true), Is.True);
        });
        await RunTicks(5);

        try
        {
            // Option on, empty hand: the click opens the backpack and leaves it worn.
            await Client.WaitPost(() => cfg.SetCVar(WornStorageCVars.ClickOpens, true));
            await ClickBackSlot();
            await RunTicks(5);
            await Server.WaitAssertion(() =>
            {
                Assert.That(ui.IsUiOpen(backpack, StorageComponent.StorageUiKey.Key, SPlayer), Is.True,
                    "The click should open the worn backpack.");
                Assert.That(inventory.TryGetSlotEntity(SPlayer, Slot, out var worn) && worn == backpack, Is.True,
                    "The click shouldn't take the backpack off.");
            });

            // Option on, item in hand: the click still puts the item inside.
            EntityUid crowbar = default;
            await Server.WaitPost(() =>
            {
                crowbar = SEntMan.SpawnEntity("Crowbar", SEntMan.GetCoordinates(PlayerCoords));
                Assert.That(hands.TryPickupAnyHand(SPlayer, crowbar), Is.True);
            });
            await RunTicks(5);
            await ClickBackSlot();
            await RunTicks(5);
            await Server.WaitAssertion(() =>
            {
                var container = SEntMan.System<SharedContainerSystem>();
                Assert.That(container.TryGetContainingContainer(crowbar, out var holder) && holder.Owner == backpack,
                    Is.True, "A click with an item in hand should put it in the backpack.");
            });

            // Dropping on the inactive empty hand takes it off into that hand and makes it active. The fixture leaves
            // the player one hand, so add a second.
            const string target = "wg_worn_storage_test_hand";
            await Server.WaitPost(() => hands.AddHand(SPlayer, target, HandLocation.Left));
            await RunTicks(5);
            await Client.WaitPost(() =>
                Client.ResolveDependency<IUserInterfaceManager>().GetUIController<InventoryUIController>()
                    .DropWornStorageOnHand(Slot, target));
            await RunTicks(10);
            await Server.WaitAssertion(() =>
            {
                var comp = SEntMan.GetComponent<HandsComponent>(SPlayer);
                Assert.That(comp.Hands[target].HeldEntity, Is.EqualTo(backpack), "The backpack should land in the dropped-on hand.");
                Assert.That(comp.ActiveHand?.Name, Is.EqualTo(target));
                Assert.That(inventory.TryGetSlotEntity(SPlayer, Slot, out _), Is.False);
            });

            // Option off: a click takes it off into the active hand, as upstream does.
            await Server.WaitPost(() =>
            {
                hands.TryDrop(SPlayer, backpack);
                Assert.That(inventory.TryEquip(SPlayer, backpack, Slot, silent: true, force: true), Is.True);
            });
            await Client.WaitPost(() => cfg.SetCVar(WornStorageCVars.ClickOpens, false));
            await RunTicks(5);
            await ClickBackSlot();
            await RunTicks(10);
            await Server.WaitAssertion(() =>
            {
                Assert.That(inventory.TryGetSlotEntity(SPlayer, Slot, out _), Is.False,
                    "With the option off a click should take the backpack off.");
                Assert.That(hands.IsHolding(SPlayer, backpack), Is.True);
            });
        }
        finally
        {
            await Client.WaitPost(() => cfg.SetCVar(WornStorageCVars.ClickOpens, false));
        }
    }

    /// <summary>
    /// With the option on, clicking a box inside an open backpack opens it in place, other items still come out on a
    /// click, and taking the box out into a hand works. Off, a click takes the box out as before.
    /// </summary>
    [Test]
    public async Task ClickOpensStoredStorage()
    {
        var hands = SEntMan.System<SharedHandsSystem>();
        var storage = SEntMan.System<SharedStorageSystem>();
        var container = SEntMan.System<SharedContainerSystem>();
        var ui = SEntMan.System<SharedUserInterfaceSystem>();
        var cfg = Client.ResolveDependency<IConfigurationManager>();

        var backpack = ToServer(await SpawnTarget("ClothingBackpack"));
        await Server.WaitPost(() => SEntMan.RemoveComponent<UseDelayComponent>(backpack));
        await RunTicks(5);
        await Activate();
        Assert.That(ui.IsUiOpen(backpack, StorageComponent.StorageUiKey.Key, SPlayer), Is.True);

        EntityUid box = default;
        EntityUid crowbar = default;
        await Server.WaitPost(() =>
        {
            box = SEntMan.SpawnEntity("BoxCardboard", SEntMan.GetCoordinates(PlayerCoords));
            crowbar = SEntMan.SpawnEntity("Crowbar", SEntMan.GetCoordinates(PlayerCoords));
            Assert.That(storage.Insert(backpack, box, out _, playSound: false), Is.True);
            Assert.That(storage.Insert(backpack, crowbar, out _, playSound: false), Is.True);
        });
        await RunTicks(5);

        bool InBackpack(EntityUid item) =>
            container.TryGetContainingContainer(item, out var holder) && holder.Owner == backpack;

        try
        {
            // Option off: a click takes the box out, as upstream does.
            await ClickStoredItem(box);
            await Server.WaitAssertion(() => Assert.That(hands.IsHolding(SPlayer, box), Is.True,
                "With the option off a click should take the box out."));
            await Server.WaitPost(() =>
            {
                hands.TryDrop(SPlayer, box);
                Assert.That(storage.Insert(backpack, box, out _, playSound: false), Is.True);
            });
            await RunTicks(5);

            // Option on: an item that isn't storage still comes out on a click.
            await Client.WaitPost(() => cfg.SetCVar(WornStorageCVars.ClickOpens, true));
            await ClickStoredItem(crowbar);
            await Server.WaitAssertion(() => Assert.That(hands.IsHolding(SPlayer, crowbar), Is.True,
                "A click should still take out an item that isn't storage."));
            await Server.WaitPost(() => SEntMan.DeleteEntity(crowbar));
            await RunTicks(5);

            // Option on, empty hand: the click opens the box and leaves it in the backpack.
            await ClickStoredItem(box);
            await Server.WaitAssertion(() =>
            {
                Assert.That(ui.IsUiOpen(box, StorageComponent.StorageUiKey.Key, SPlayer), Is.True,
                    "The click should open the stored box.");
                Assert.That(InBackpack(box), Is.True, "The click shouldn't take the box out.");
            });

            // Taking it out into a named empty hand puts it there and makes that hand active.
            const string target = "wg_stored_storage_test_hand";
            await Server.WaitPost(() => hands.AddHand(SPlayer, target, HandLocation.Left));
            await RunTicks(5);
            var cBox = CEntMan.GetEntity(SEntMan.GetNetEntity(box));
            var cBackpack = CEntMan.GetEntity(SEntMan.GetNetEntity(backpack));
            await Client.WaitPost(() =>
                Client.ResolveDependency<IUserInterfaceManager>().GetUIController<StorageUIController>()
                    .TakeOutStoredItem(cBox, cBackpack, target));
            await RunTicks(10);
            await Server.WaitAssertion(() =>
            {
                var comp = SEntMan.GetComponent<HandsComponent>(SPlayer);
                Assert.That(comp.Hands[target].HeldEntity, Is.EqualTo(box), "The box should land in the named hand.");
                Assert.That(comp.ActiveHand?.Name, Is.EqualTo(target));
            });
        }
        finally
        {
            await Client.WaitPost(() => cfg.SetCVar(WornStorageCVars.ClickOpens, false));
        }
    }

    private async Task ClickBackSlot()
    {
        var button = GetControlFromChildren<SlotControl>(slot => slot.SlotName == Slot, GetWidget<HotbarGui>());
        await ClickControl(button.ButtonRect);
    }

    /// <summary>
    /// Clicks the piece for a server entity in the open storage window, the way StorageInteractionTest does.
    /// </summary>
    private async Task ClickStoredItem(EntityUid item)
    {
        var uid = CEntMan.GetEntity(SEntMan.GetNetEntity(item));
        var storageContainer = GetControlFromField<Control>(nameof(HotbarGui.SingleStorageContainer), GetWidget<HotbarGui>());
        var piece = GetControlFromChildren<ItemGridPiece>(c => c.Entity == uid, storageContainer);
        await ClickControl(piece, ContentKeyFunctions.MoveStoredItem);
        await RunTicks(10);
    }
}
