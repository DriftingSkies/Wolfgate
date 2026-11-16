using Content.Shared.Clothing.Components;
using Robust.Shared.Containers;

namespace Content.Shared.Clothing.EntitySystems;

/// <summary>
/// Lets the MODsuit seal recompute a worn part's hidden layers without relying on equip events.
/// </summary>
public sealed partial class HideLayerClothingSystem
{
    [Dependency] private SharedContainerSystem _container = default!;

    public void SetWearerLayersHidden(EntityUid clothing, bool hide)
    {
        if (!TryComp<HideLayerClothingComponent>(clothing, out var hideComp)
            || !TryComp<ClothingComponent>(clothing, out var clothingComp)
            || clothingComp.InSlot == null
            || !_container.TryGetContainingContainer((clothing, null, null), out var container))
            return;

        SetLayerVisibility((clothing, hideComp, clothingComp), container.Owner, hide);
    }
}
