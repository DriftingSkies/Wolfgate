using Content.Shared._WG.Drydock;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Drydock;

/// <summary>
/// The refits a ship owns. Saved with the ship, so they outlast its parts: a repair brings what falls short back to tier.
/// </summary>
[RegisterComponent]
public sealed partial class WGShipRefitsComponent : Component
{
    /// <summary>One entry per refit line owned.</summary>
    [DataField]
    public List<WGShipRefit> Refits = new();
}

/// <summary>A refit line a ship owns, and how many of its units were paid for.</summary>
[DataDefinition]
public sealed partial class WGShipRefit
{
    /// <summary>Refit line.</summary>
    [DataField(required: true)]
    public ProtoId<WGShipRefitPrototype> Refit;

    /// <summary>Tier owned, from 1.</summary>
    [DataField]
    public int Tier;

    /// <summary>Units paid for at <see cref="Tier"/>; restoring more than these is charged in full.</summary>
    [DataField]
    public int Coverage;
}
