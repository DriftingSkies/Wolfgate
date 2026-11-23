using Content.Shared._WG.ShipModules;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// The modules fitted into a ship's slots, on its grid. Saved with the ship; charges and timers aren't, so a loaded
/// ship starts full.
/// </summary>
[RegisterComponent]
public sealed partial class WGShipModuleBayComponent : Component
{
    /// <summary>One entry per filled slot.</summary>
    [DataField]
    public List<WGFittedModule> Fitted = new();

    /// <summary>When a shield field was last seen raised on this ship.</summary>
    [ViewVariables]
    public TimeSpan? LastShielded;
}

/// <summary>A module in one of a ship's slots, with its charges.</summary>
[DataDefinition]
public sealed partial class WGFittedModule
{
    /// <summary>Slot index, from 0.</summary>
    [DataField(required: true)]
    public int Slot;

    /// <summary>The module fitted in the slot.</summary>
    [DataField(required: true)]
    public ProtoId<WGShipModulePrototype> Module;

    /// <summary>Uses left; -1 until the module first refills, which fills it.</summary>
    [ViewVariables]
    public int Charges = -1;

    /// <summary>When the next charge arrives, while below the module's maximum.</summary>
    [ViewVariables]
    public TimeSpan NextCharge;

    /// <summary>When the cooldown from the last use ends.</summary>
    [ViewVariables]
    public TimeSpan ReadyAt;
}
