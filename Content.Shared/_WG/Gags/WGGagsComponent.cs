using Robust.Shared.GameStates;
using Content.Shared.VendingMachines;

namespace Content.Shared._WG.Gags;

/// <summary>
/// An item drawn in recolourable layers, after SS13's greyscale sprites (GAGS). A sprite layer whose state ends in
/// <c>-c&lt;N&gt;</c> is tinted with colour N, on the item, on the wearer and in hand.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WGGagsComponent : Component
{
    /// <summary>One colour per slot; the prototype's are the defaults.</summary>
    [DataField(required: true), AutoNetworkedField]
    public List<Color> Colors = new();
}

/// <summary>Provenance that lets a vending machine accept its dispensed item back into stock. Not saved, so a reloaded item can't be returned.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGGagsVendorReturnItemComponent : Component
{
    public EntityUid Vendor;
    public InventoryType Type;
    public string ItemId = string.Empty;
}
