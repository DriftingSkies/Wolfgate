using Robust.Shared.GameStates;

namespace Content.Shared._WG.Vehicles.Bison;

/// <summary>Cargo riding on a Bison's bed; the client draws it above the carrier.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CMUCargoOnBedComponent : Component
{
    /// <summary>The draw depth the sprite had before it was loaded.</summary>
    [ViewVariables]
    public int? OldDrawDepth;

    /// <summary>Whether the sprite ignored rotation before it was loaded.</summary>
    [ViewVariables]
    public bool OldNoRotation;
}
