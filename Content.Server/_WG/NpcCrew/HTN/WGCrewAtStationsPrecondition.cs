using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>Met while the crewman's ship is at battle readiness and his duty is not the helm or the guns.</summary>
public sealed partial class WGCrewAtStationsPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return _entManager.System<WGCrewReadinessSystem>().AtStations(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
