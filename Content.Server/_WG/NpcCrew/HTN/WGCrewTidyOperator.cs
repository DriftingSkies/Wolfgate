using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>Plans a janitor's walk to the rubbish he is after or the bin he takes it to, and picks up or bins there.</summary>
public sealed partial class WGCrewTidyOperator : HTNOperator
{
    private WGCrewTidySystem _tidy = default!;

    [DataField]
    public bool Perform;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _tidy = sysManager.GetEntitySystem<WGCrewTidySystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!_tidy.Destination(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var destination))
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));
        return Task.FromResult<(bool, Dictionary<string, object>?)>((true, new Dictionary<string, object>
        {
            { "WGCrewTidyDestination", destination },
        }));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return !Perform || _tidy.Perform(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner))
            ? HTNOperatorStatus.Finished : HTNOperatorStatus.Continuing;
    }
}
