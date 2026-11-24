using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>Plans a crewman's walk to a piece of his own kit lying about, and puts it back on there.</summary>
public sealed partial class WGCrewRedressOperator : HTNOperator
{
    private WGCrewOutfitSystem _outfits = default!;

    [DataField]
    public bool Perform;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _outfits = sysManager.GetEntitySystem<WGCrewOutfitSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!_outfits.Destination(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var destination))
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));
        return Task.FromResult<(bool, Dictionary<string, object>?)>((true, new Dictionary<string, object>
        {
            { "WGCrewRedressDestination", destination },
        }));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return !Perform || _outfits.Perform(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner))
            ? HTNOperatorStatus.Finished : HTNOperatorStatus.Continuing;
    }
}
