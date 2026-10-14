using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetSignals;

/// <summary>On a sector body: the vessels and sites on its world, so radars in space and on every layer can show them.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFPlanetSignalsComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public List<WFPlanetSignal> Signals = new();
}

/// <summary>
/// One thing on a world. A site's name is its Fluent id, empty until the site is identified; a vessel's is its own.
/// <paramref name="Expedition"/> is the grid of the ship running an expedition on a site.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WFPlanetSignal(
    WFPlanetSignalKind Kind,
    string Name,
    Vector2i Position,
    NetEntity Map,
    WFPlanetSignalLayer Layer,
    NetEntity? Expedition);

public enum WFPlanetSignalKind : byte { Vessel, Site }

/// <summary>Which layer of the world a signal is on.</summary>
public enum WFPlanetSignalLayer : byte { Ground, Air, Orbit }
