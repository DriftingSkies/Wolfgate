using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WF.Xenonids.Parasite.AI;

/// <summary>HTN operator that leaps at the entity under <see cref="TargetKey"/> through <see cref="WFNPCLeapComponent"/>. Ported from RMC-14's LeapOperator.</summary>
public sealed partial class WFLeapOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private IEntityManager _entManager = default!;

    [DataField("shutdownState")]
    public HTNPlanState ShutdownState { get; private set; } = HTNPlanState.TaskFinished;

    [DataField("targetKey", required: true)]
    public string TargetKey = default!;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        return (blackboard.TryGetValue<EntityUid>(TargetKey, out _, _entManager), null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var leap = _entManager.EnsureComponent<WFNPCLeapComponent>(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        leap.Target = blackboard.GetValue<EntityUid>(TargetKey);
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _entManager.RemoveComponent<WFNPCLeapComponent>(owner);
        blackboard.Remove<EntityUid>(TargetKey);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        base.Update(blackboard, frameTime);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<WFNPCLeapComponent>(owner, out var leap) ||
            !blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
        {
            return HTNOperatorStatus.Failed;
        }

        leap.Target = target;
        var status = leap.Status switch
        {
            WFLeapStatus.Normal => HTNOperatorStatus.Continuing,
            WFLeapStatus.Finished => HTNOperatorStatus.Finished,
            _ => HTNOperatorStatus.Failed,
        };

        if (status == HTNOperatorStatus.Continuing && ShutdownState == HTNPlanState.PlanFinished)
            status = HTNOperatorStatus.Finished;

        return status;
    }
}
