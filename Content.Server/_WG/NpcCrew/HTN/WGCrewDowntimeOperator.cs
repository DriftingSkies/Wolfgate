using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>Plans the next place a crewman on a break walks to, and does that step of the break once there.</summary>
public sealed partial class WGCrewDowntimeOperator : HTNOperator
{
    private WGCrewDowntimeSystem _downtime = default!;

    [DataField]
    public bool Perform;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _downtime = sysManager.GetEntitySystem<WGCrewDowntimeSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!_downtime.Destination(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var destination))
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));
        return Task.FromResult<(bool, Dictionary<string, object>?)>((true, new Dictionary<string, object>
        {
            { "WGCrewDowntimeDestination", destination },
        }));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return !Perform || _downtime.Perform(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner))
            ? HTNOperatorStatus.Finished : HTNOperatorStatus.Continuing;
    }
}
