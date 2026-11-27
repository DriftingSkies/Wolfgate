namespace Content.Server._WG.NetTraffic;

/// <summary>
/// Raised each tick by <see cref="WGMapScopedPvsSystem"/>: maps added to <see cref="Maps"/> keep their grids sent to
/// everyone instead of only to players looking at them.
/// </summary>
[ByRefEvent]
public readonly record struct WGMapScopeExemptEvent(HashSet<EntityUid> Maps);
