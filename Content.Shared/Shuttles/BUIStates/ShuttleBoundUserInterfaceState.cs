using Content.Shared.Shuttles.UI.MapObjects;
using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.BUIStates;

[Serializable, NetSerializable]
public sealed class ShuttleBoundUserInterfaceState : BoundUserInterfaceState
{
    // WOLFGATE(Cockpit) START: include confirmed autopilot activity in the initial helm state.
    /// <summary>Null means this console has no available autopilot.</summary>
    public bool? CockpitAutopilotActive;
    // WOLFGATE END
    public NavInterfaceState NavState;
    public ShuttleMapInterfaceState MapState;
    public DockingInterfaceState DockState;
    // WOLFGATE(ShipShields) START: expose the ship's authoritative shield allocation at every helm.
    public Content.Shared._WF.ShipShields.WFShipShieldShuntState? ShieldShunt;
    // WOLFGATE END
    // WOLFGATE(TractorBeam) START: expose active tractor sources to the captured ship's helm
    /// <summary>Names of the distinct ships currently applying an active tractor beam to this shuttle.</summary>
    public string[] TractorSources;
    // WOLFGATE END

    public ShuttleBoundUserInterfaceState(NavInterfaceState navState, ShuttleMapInterfaceState mapState, DockingInterfaceState dockState, // WOLFGATE(TractorBeam)
        string[]? tractorSources = null) // WOLFGATE(TractorBeam)
    {
        NavState = navState;
        MapState = mapState;
        DockState = dockState;
        TractorSources = tractorSources ?? Array.Empty<string>(); // WOLFGATE(TractorBeam)
    }
}
