using Content.Client.Hands.Systems;
using Content.Client.UserInterface.Systems.Hands.Controls;
using Content.Client.UserInterface.Systems.Storage.Controls;
using Content.Shared._WG.WornStorage;
using Content.Shared.Storage;
using Robust.Client.UserInterface;

namespace Content.Client.UserInterface.Systems.Storage;

/// <summary>
/// Worn storage click for storage inside an open storage window: a click opens it, and dragging it out takes it out.
/// </summary>
public sealed partial class StorageUIController
{
    /// <summary>
    /// Opens a clicked storage item in place, instead of taking it out, when the hand is empty. Returns true if it did.
    /// </summary>
    private bool TryOpenStoredStorage(EntityUid item, EntityUid storage)
    {
        if (!_configuration.GetCVar(WornStorageCVars.ClickOpens)
            || !_storage.NestedStorage
            || !EntityManager.TryGetComponent(item, out StorageComponent? itemStorage)
            || !itemStorage.OpenOnActivate
            || EntityManager.System<HandsSystem>().GetActiveHandEntity() != null)
            return false;

        EntityManager.RaisePredictiveEvent(new OpenNestedStorageEvent(
            EntityManager.GetNetEntity(item),
            EntityManager.GetNetEntity(storage)));
        return true;
    }

    /// <summary>
    /// Takes out a storage item that was dragged from its window and released outside every storage window.
    /// </summary>
    private void DropStoredStorageOutside(Control? target, EntityUid item, EntityUid storage)
    {
        if (!_configuration.GetCVar(WornStorageCVars.ClickOpens) || !EntityManager.HasComponent<StorageComponent>(item))
            return;

        HandButton? handButton = null;
        for (var current = target; current != null; current = current.Parent)
        {
            if (current is StorageWindow)
                return;

            handButton ??= current as HandButton;
        }

        TakeOutStoredItem(item, storage, handButton?.SlotName);
    }

    /// <summary>
    /// Takes a stored item out into the named hand, or the active hand if none is named, if that hand is empty.
    /// </summary>
    public void TakeOutStoredItem(EntityUid item, EntityUid storage, string? handName = null)
    {
        var handsSystem = EntityManager.System<HandsSystem>();
        if (_player.LocalEntity is not { } player || !handsSystem.TryGetPlayerHands(out var hands))
            return;

        if (handName != null)
        {
            if (!handsSystem.TryGetHand(player, handName, out var hand, hands) || hand.HeldEntity != null)
                return;

            // Make the target hand active so the item lands in it.
            if (hands.ActiveHand != hand)
                handsSystem.UIHandClick(hands, handName);
        }
        else if (hands.ActiveHandEntity != null)
        {
            // A held item would be used on the stored one instead.
            return;
        }

        EntityManager.RaisePredictiveEvent(new StorageInteractWithItemEvent(
            EntityManager.GetNetEntity(item),
            EntityManager.GetNetEntity(storage)));
    }
}
