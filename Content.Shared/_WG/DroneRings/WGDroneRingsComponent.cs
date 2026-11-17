using Robust.Shared.GameStates;

namespace Content.Shared._WG.DroneRings;

/// <summary>On a map: the distances from its centre at which drones start to spawn, drawn as rings on radars.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WGDroneRingsComponent : Component
{
    /// <summary>Ring radii in metres, nearest first. Each is where a new mix of drones begins.</summary>
    [DataField, AutoNetworkedField]
    public List<float> Radii = new();
}
