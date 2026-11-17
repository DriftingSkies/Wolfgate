using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>
/// Covers the approach to a hostile the crewman knows of but may not go after: weapon drawn and loaded, standing where
/// he is, facing its way, until he can fight it or it no longer needs covering.
/// </summary>
public sealed partial class WGCrewBraceOperator : HTNOperator
{
    /// <summary>The hostile being braced for.</summary>
    public const string TargetKey = "WGCrewBraceTarget";

    [Dependency] private IEntityManager _entManager = default!;
    private WGCrewWeaponSystem _weapons = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _weapons = sysManager.GetEntitySystem<WGCrewWeaponSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_weapons.Provoked(owner) || _weapons.BraceTarget(owner) is not { } target)
            return (false, null);

        return (true, new Dictionary<string, object> { { TargetKey, target } });
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return;

        _weapons.Brace((owner, _entManager.EnsureComponent<WGCrewWeaponComponent>(owner)), target);
        _weapons.TryDraw(owner);
        _weapons.TryReloadOrSwitch(owner);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager)
            || !_entManager.TryGetComponent<WGCrewWeaponComponent>(owner, out var weapon))
            return HTNOperatorStatus.Failed;

        return _weapons.HoldBrace((owner, weapon), target) ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Finished;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        blackboard.Remove<EntityUid>(TargetKey);
    }
}
