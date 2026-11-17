using Robust.Shared.GameStates;

namespace Content.Shared.Station.Components;

/// <summary>
/// Stores core information about a station, namely its config and associated grids.
/// All station entities will have this component.
/// </summary>
// WOLFGATE(Performance): was AutoGenerateComponentState; WGDeletedRefStateSystem sends Grids, whose entries can be deleted first
[RegisterComponent, NetworkedComponent, Access(typeof(SharedStationSystem), typeof(Content.Shared._WG.Performance.WGDeletedRefStateSystem))]
public sealed partial class StationDataComponent : Component
{
    /// <summary>
    /// The game map prototype, if any, associated with this station.
    /// </summary>
    [DataField]
    public StationConfig? StationConfig;

    /// <summary>
    /// List of all grids this station is part of.
    /// </summary>
    [DataField] // WOLFGATE(Performance): networked by WGDeletedRefStateSystem
    public HashSet<EntityUid> Grids = new();
}
