using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Eats or cocoons the target corpse with the mob's <see cref="FaunaCorpseEaterComponent"/>, running until it's done.
/// </summary>
public sealed partial class FaunaEatCorpseOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private FaunaCorpseEaterSystem _eater = default!;

    /// <summary>
    /// Blackboard key that holds the corpse to eat.
    /// </summary>
    [DataField]
    public string TargetKey = "Target";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _eater = sysManager.GetEntitySystem<FaunaCorpseEaterSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<FaunaCorpseEaterComponent>(owner, out var eater) ||
            !blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return HTNOperatorStatus.Failed;

        if (eater.Eating == target)
            return HTNOperatorStatus.Continuing;

        if (eater.Eating != null && _entManager.Deleted(eater.Eating))
            eater.Eating = null;

        if (!_eater.CanEat((owner, eater), target))
            return HTNOperatorStatus.Finished;

        return _eater.TryStartEating((owner, eater), target)
            ? HTNOperatorStatus.Continuing
            : HTNOperatorStatus.Failed;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        blackboard.Remove<EntityUid>(TargetKey);
    }
}
