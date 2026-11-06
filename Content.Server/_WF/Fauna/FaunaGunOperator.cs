using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Runs a gun operator but finishes once the mob's own gun is empty, so the HTN can fall back to melee.
/// </summary>
public sealed partial class FaunaGunOperator : HTNOperator, IHtnConditionalShutdown
{
    [Dependency] private IEntityManager _entManager = default!;

    private GunSystem _gun = default!;

    /// <summary>
    /// The gun operator this one wraps.
    /// </summary>
    [DataField(required: true)]
    public HTNOperator Gun = default!;

    public HTNPlanState ShutdownState => (Gun as IHtnConditionalShutdown)?.ShutdownState ?? HTNPlanState.TaskFinished;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        Gun.Initialize(sysManager);
        _gun = sysManager.GetEntitySystem<GunSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        return Gun.Plan(blackboard, cancelToken);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        Gun.Startup(blackboard);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_gun.TryGetGun(owner, out var gun))
        {
            var ammo = new GetAmmoCountEvent();
            _entManager.EventBus.RaiseLocalEvent(gun, ref ammo);
            if (ammo.Count == 0)
                return HTNOperatorStatus.Finished;
        }

        return Gun.Update(blackboard, frameTime);
    }

    /// <summary>
    /// Passes the conditional shutdown on to <see cref="Gun"/>.
    /// </summary>
    public void ConditionalShutdown(NPCBlackboard blackboard)
    {
        (Gun as IHtnConditionalShutdown)?.ConditionalShutdown(blackboard);
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        Gun.PlanShutdown(blackboard);
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        Gun.TaskShutdown(blackboard, status);
    }
}
