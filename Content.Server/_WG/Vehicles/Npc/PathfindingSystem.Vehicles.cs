using Content.Shared._WG.Vehicles.Base;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /// <summary>
    /// Whether an entity blocks every tile its hull covers, not just the one its middle stands on: a vehicle is several
    /// tiles long, and mobs pathing through the rest of it walk into its side and stick there.
    /// </summary>
    private bool WgCoversTiles(EntityUid uid)
    {
        return HasComp<RMCVehicleComponent>(uid);
    }
}
