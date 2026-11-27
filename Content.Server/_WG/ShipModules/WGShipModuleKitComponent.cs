using Content.Shared._WG.ShipModules;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// A ship module kit printed after its research node: a drydock fitting that module uses up one carried aboard the
/// ship.
/// </summary>
[RegisterComponent]
public sealed partial class WGShipModuleKitComponent : Component
{
    /// <summary>The module this kit lets a drydock fit.</summary>
    [DataField(required: true)]
    public ProtoId<WGShipModulePrototype> Module;
}
