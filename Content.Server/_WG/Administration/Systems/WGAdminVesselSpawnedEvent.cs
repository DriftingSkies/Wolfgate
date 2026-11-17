using Robust.Shared.Player;

namespace Content.Server._WG.Administration.Systems;

/// <summary>
/// Raised once the admin Spawn Vessel command has spawned a vessel, registered its owner and named it, so modules can
/// set the new ship up: ShipCommand applies a preset (the one named with <c>preset:&lt;name&gt;</c>, else a default).
/// </summary>
/// <param name="Grid">The vessel's grid.</param>
/// <param name="Vessel">The vessel prototype it was spawned from.</param>
/// <param name="Spawner">The admin who spawned it, if a player did.</param>
/// <param name="Owner">The player it was registered to, if any.</param>
/// <param name="Preset">The preset named on the command line, if any.</param>
public sealed record WGAdminVesselSpawnedEvent(EntityUid Grid, string Vessel, ICommonSession? Spawner, ICommonSession? Owner, string? Preset);
