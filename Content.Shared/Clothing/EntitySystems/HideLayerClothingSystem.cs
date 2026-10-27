using Content.Shared.Clothing.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared.Clothing.EntitySystems;

public sealed partial class HideLayerClothingSystem : EntitySystem
{
    [Dependency] private SharedHumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<HideLayerClothingComponent, ClothingGotUnequippedEvent>(OnHideGotUnequipped);
        SubscribeLocalEvent<HideLayerClothingComponent, ClothingGotEquippedEvent>(OnHideGotEquipped);
        SubscribeLocalEvent<HideLayerClothingComponent, ItemMaskToggledEvent>(OnHideToggled);
        // WOLFGATE START: a modsuit seal adds and removes this on a worn helmet, which raises no equip events
        // Without these the seal never hid the layers, and removing it from a helmet that hid them left them hidden for good.
        SubscribeLocalEvent<HideLayerClothingComponent, ComponentStartup>(OnHideStartup);
        SubscribeLocalEvent<HideLayerClothingComponent, ComponentShutdown>(OnHideShutdown);
        // WOLFGATE END
    }

    // WOLFGATE START: layers follow the component while the clothing is worn
    private void OnHideStartup(Entity<HideLayerClothingComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<ClothingComponent>(ent, out var clothing) && clothing.InSlot != null)
            SetLayerVisibility((ent, ent.Comp, clothing), Transform(ent).ParentUid, hideLayers: true);
    }

    private void OnHideShutdown(Entity<HideLayerClothingComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<ClothingComponent>(ent, out var clothing) || clothing.InSlot == null)
            return;

        var wearer = Transform(ent).ParentUid;
        if (!TerminatingOrDeleted(wearer))
            SetLayerVisibility((ent, ent.Comp, clothing), wearer, hideLayers: false);
    }
    // WOLFGATE END

    private void OnHideToggled(Entity<HideLayerClothingComponent> ent, ref ItemMaskToggledEvent args)
    {
        if (args.Wearer != null)
            SetLayerVisibility(ent!, args.Wearer.Value, hideLayers: true);
    }

    private void OnHideGotEquipped(Entity<HideLayerClothingComponent> ent, ref ClothingGotEquippedEvent args)
    {
        SetLayerVisibility(ent!, args.Wearer, hideLayers: true);
    }

    private void OnHideGotUnequipped(Entity<HideLayerClothingComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        SetLayerVisibility(ent!, args.Wearer, hideLayers: false);
    }

    private void SetLayerVisibility(
        Entity<HideLayerClothingComponent?, ClothingComponent?> clothing,
        EntityUid user,
        bool hideLayers)
    {
        if (_timing.ApplyingState)
            return;

        if (!Resolve(clothing.Owner, ref clothing.Comp1, ref clothing.Comp2))
            return;

        if (!TryComp<HumanoidAppearanceComponent>(user, out var humanoid))
            return;
		
		var humanoidEntity = new Entity<HumanoidAppearanceComponent>(user, humanoid);

        hideLayers &= IsEnabled(new Entity<HideLayerClothingComponent, ClothingComponent>( clothing.Owner, clothing.Comp1!, clothing.Comp2! ));

        var hideable = humanoid.HideLayersOnEquip;
		
        var inSlot = clothing.Comp2.InSlotFlag ?? SlotFlags.NONE;

        // This method should only be getting called while the clothing is equipped (though possibly currently in
        // the process of getting unequipped).
        // WOLFGATE START: a client that first saw the wearer already dressed never got the equip for this item (the
        // wearer's container state lands before its InventoryComponent has slots, so InventorySystem.OnEntInserted
        // finds none), and InSlot stays null there. Release skipped the asserts below; Debug closed the client on
        // the unequip. The server's HiddenLayers state carries the change, so the client has nothing to predict.
        if (clothing.Comp2.InSlot == null || clothing.Comp2.InSlotFlag == null)
            return;
        // WOLFGATE END

        DebugTools.AssertNotNull(clothing.Comp2.InSlot);
        DebugTools.AssertNotNull(clothing.Comp2.InSlotFlag);
        DebugTools.AssertNotEqual(inSlot, SlotFlags.NONE);

        var dirty = false;

        // iterate the HideLayerClothingComponent's layers map and check that
        // the clothing is (or was)equipped in a matching slot.
        foreach (var (layer, validSlots) in clothing.Comp1.Layers)
        {
            if (!hideable.Contains(layer))
                continue;

            // Only update this layer if we are currently equipped to the relevant slot.
            if (validSlots.HasFlag(inSlot))
                _humanoid.SetLayerVisibility(humanoidEntity, layer, !hideLayers, inSlot, ref dirty);
        }

        // Fallback for obsolete field: assume we want to hide **all** layers, as long as we are equipped to any
        // relevant clothing slot
#pragma warning disable CS0618 // Type or member is obsolete
        if (clothing.Comp1.Slots is { } slots && clothing.Comp2.Slots.HasFlag(inSlot))
#pragma warning restore CS0618 // Type or member is obsolete
        {
            foreach (var layer in slots)
            {
                if (hideable.Contains(layer))
                    _humanoid.SetLayerVisibility(humanoidEntity, layer, !hideLayers, inSlot, ref dirty);
            }
        }

        if (dirty)
            Dirty(user, humanoid);
    }

    private bool IsEnabled(Entity<HideLayerClothingComponent, ClothingComponent> clothing)
    {
        // TODO Generalize this
        // I.e., make this and mask component use some generic toggleable.

        if (!clothing.Comp1.HideOnToggle)
            return true;

        if (!TryComp(clothing, out MaskComponent? mask))
            return true;

        return !mask.IsToggled;
    }
}
