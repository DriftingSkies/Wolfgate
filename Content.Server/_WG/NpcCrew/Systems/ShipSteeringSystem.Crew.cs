using Content.Server._WG.NpcCrew.Components;
using Content.Shared._WG.NpcCrew;

namespace Content.Server._Mono.NPC.HTN;

public sealed partial class ShipSteeringSystem
{
    /// <summary>
    /// Crew must avoid the destination hull too, except the grid they dock with: the approach only steers at it once
    /// the run to the port is clear, and final docking already disables avoidance.
    /// </summary>
    private bool CrewAvoidsTarget(EntityUid? pilot) => pilot is { } uid && TryComp<WGPilotDutyComponent>(uid, out var duty)
        && duty.Orders != WGPilotOrder.Dock;

    /// <summary>The fixed heading a crew pilot is holding, if any.</summary>
    private Angle? CrewHeading(EntityUid pilot) => TryComp<WGPilotDutyComponent>(pilot, out var duty) ? duty.HeadingOverride : null;
}
