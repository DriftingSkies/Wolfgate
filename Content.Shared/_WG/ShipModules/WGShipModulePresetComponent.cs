using Robust.Shared.Prototypes;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// Mapping marker: when its ship spawns, fits a module into the ship's slots for free and deletes itself.
/// </summary>
[RegisterComponent]
public sealed partial class WGShipModulePresetComponent : Component
{
    /// <summary>The module fitted.</summary>
    [DataField(required: true)]
    public ProtoId<WGShipModulePrototype> Module;

    /// <summary>Hotkey slot to fit it in, 1 to 3; the first free slot when unset.</summary>
    [DataField]
    public int? Slot;
}
