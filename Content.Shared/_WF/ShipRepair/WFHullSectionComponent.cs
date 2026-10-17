using Robust.Shared.GameStates;

namespace Content.Shared._WF.ShipRepair;

/// <summary>A grid split off a hull; the SRD reattaches it to the hull that holds the repair snapshot.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFHullSectionComponent : Component
{
    /// <summary>The hull holding the repair snapshot.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Hull;

    /// <summary>Added to a tile index on the section, gives the index that tile has on the hull.</summary>
    [DataField, AutoNetworkedField]
    public Vector2i Offset;
}
