using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Drydock;

/// <summary>
/// A refit that replaces a ship's stock hull walls with plated ones in place, such as plastitanium with durasteel. Its
/// units are walls; it has a single tier.
/// </summary>
public sealed partial class WGHullPlatingRefit : WGShipRefitEffect<WGHullPlatingRefit>
{
    /// <summary>Each stock wall and the plated wall that replaces it.</summary>
    [DataField(required: true)]
    public Dictionary<EntProtoId, EntProtoId> Plating = new();
}
