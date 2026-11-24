using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>Plans where a bartender walks for the order under way or an empty glass and, once there, does the next step.</summary>
public sealed partial class WGCrewBartenderOperator : HTNOperator
{
    private WGCrewBartenderSystem _bartenders = default!;

    /// <summary>Whether this step does the work; off, it only plans the walk.</summary>
    [DataField]
    public bool Perform;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _bartenders = sysManager.GetEntitySystem<WGCrewBartenderSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!_bartenders.Destination(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var destination))
            return (false, null);
        return (true, new Dictionary<string, object> { { "WGCrewBartenderDestination", destination } });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return !Perform || _bartenders.Perform(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner))
            ? HTNOperatorStatus.Finished : HTNOperatorStatus.Continuing;
    }
}
