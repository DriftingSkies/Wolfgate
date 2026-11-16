using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WF.NpcCrew.HTN;

/// <summary>
/// A crewman whose skill sidesteps steps to an adjacent tile every <see cref="JukeDuration"/> while he shoots, through
/// the upstream <see cref="NPCJukeComponent"/>. Others stand still as before. Ends with the plan.
/// </summary>
public sealed partial class WFCrewSidestepOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private IEntityManager _entManager = default!;

    /// <summary>Seconds between sidesteps, and the longest one takes.</summary>
    [DataField]
    public float JukeDuration = 1.5f;

    public HTNPlanState ShutdownState => HTNPlanState.PlanFinished;

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<WFCrewComponent>(owner, out var crew) || !WFCrewSkills.Of(crew).Sidesteps)
            return;

        var juke = _entManager.EnsureComponent<NPCJukeComponent>(owner);
        juke.JukeType = JukeType.AdjacentTile;
        juke.JukeDuration = JukeDuration;
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }

    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        _entManager.RemoveComponent<NPCJukeComponent>(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
