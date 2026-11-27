using Robust.Shared.Map;

namespace Content.Server._WG.ShipFacing;

/// <summary>On a pilot holding the facing key: the point their ship turns toward.</summary>
[RegisterComponent, Access(typeof(WGShipFacingSystem))]
public sealed partial class WGShipFacingComponent : Component
{
    [ViewVariables]
    public MapCoordinates Target;

    /// <summary>The helm the point was sent from; it means nothing at any other.</summary>
    [ViewVariables]
    public EntityUid Console;

    /// <summary>When the point lapses unless the client sends it again.</summary>
    [ViewVariables]
    public TimeSpan Expires;
}
