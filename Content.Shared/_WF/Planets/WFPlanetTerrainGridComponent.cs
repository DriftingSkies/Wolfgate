using Robust.Shared.GameStates;

namespace Content.Shared._WF.Planets;

/// <summary>
/// A grid on a planet layer that is buildings on the ground, not a vessel, such as an outpost: rained on by its own
/// roofs rather than covered throughout, casting no ship's shadow, and seen from inside like the ground's buildings.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFPlanetTerrainGridComponent : Component;
