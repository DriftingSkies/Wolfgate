using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>Plans where a chef walks for the dish he is making and, once there, does one step of it.</summary>
public sealed partial class WGCrewChefOperator : HTNOperator
{
    private WGCrewChefSystem _chefs = default!;

    /// <summary>Whether this step does the work; off, it only plans the walk.</summary>
    [DataField]
    public bool Perform;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _chefs = sysManager.GetEntitySystem<WGCrewChefSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!_chefs.Destination(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var destination))
            return (false, null);
        return (true, new Dictionary<string, object> { { "WGCrewChefDestination", destination } });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return !Perform || _chefs.Perform(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner))
            ? HTNOperatorStatus.Finished : HTNOperatorStatus.Continuing;
    }
}
