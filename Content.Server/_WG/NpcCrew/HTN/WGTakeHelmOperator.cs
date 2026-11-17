using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared._WG.NpcCrew;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>
/// Takes the helm picked into the blackboard and holds it for as long as the pilot stays attached. Orders are flown
/// by <see cref="WGPilotDutySystem"/>; this operator only owns the attachment.
/// </summary>
public sealed partial class WGTakeHelmOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;

    private WGPilotDutySystem _pilot = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pilot = sysManager.GetEntitySystem<WGPilotDutySystem>();
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);

        if (blackboard.TryGetValue<EntityUid>(WGPilotDutySystem.HelmKey, out var helm, _entManager))
            _pilot.TryTakeHelm(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), helm);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // A new duty isn't a better branch, so the planner would keep this plan forever; let go here.
        if (blackboard.TryGetValue<string>(WGCrewSystem.DutyKey, out var duty, _entManager) && duty != WGCrewDuties.Pilot)
        {
            _pilot.ReleaseHelm(owner);
            return HTNOperatorStatus.Failed;
        }

        return _pilot.IsAtHelm(owner) ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Failed;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        // A plain failure is also how an NPC is put to sleep when no player is near. The ship keeps flying then;
        // WGPilotDutySystem lets go of the helm itself once the pilot can't hold it.
        if (status == HTNOperatorStatus.Failed && _pilot.CanHoldHelm(owner))
            return;

        _pilot.ReleaseHelm(owner);
    }
}
