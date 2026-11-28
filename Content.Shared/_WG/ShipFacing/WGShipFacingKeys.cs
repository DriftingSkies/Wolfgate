using Robust.Shared.Input;

namespace Content.Shared._WG.ShipFacing;

/// <summary>The cursor-facing key, bound in the pilot's shuttle input context.</summary>
[KeyFunctions]
public static class WGShipFacingKeys
{
    /// <summary>Held, turns the flown ship's nose toward the cursor.</summary>
    public static readonly BoundKeyFunction WGShuttleFaceCursor = "WGShuttleFaceCursor";
}
