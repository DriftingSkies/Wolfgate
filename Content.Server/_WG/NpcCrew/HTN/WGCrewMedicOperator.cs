using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>Plans where a medic walks for his patient and, once there, works on them a step at a time.</summary>
public sealed partial class WGCrewMedicOperator : HTNOperator
{
    private WGCrewMedicSystem _medics = default!;

    /// <summary>Whether this step works on the patient; off, it only plans the walk to them.</summary>
    [DataField]
    public bool Perform;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _medics = sysManager.GetEntitySystem<WGCrewMedicSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!_medics.Destination(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var destination))
            return (false, null);
        return (true, new Dictionary<string, object> { { "WGCrewMedicDestination", destination } });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return !Perform || _medics.Perform(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner))
            ? HTNOperatorStatus.Finished : HTNOperatorStatus.Continuing;
    }
}
