using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>Selects an onboard hostile without scavenging weapons or pursuing other grids.</summary>
public sealed partial class WGCrewTargetOperator : HTNOperator
{
    private WGCrewWeaponSystem _weapons = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _weapons = sysManager.GetEntitySystem<WGCrewWeaponSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_weapons.PickTarget(owner) is not { } target)
            return (false, null);
        return (true, new Dictionary<string, object>
        {
            { "Target", target },
            { "TargetCoordinates", new EntityCoordinates(target, Vector2.Zero) },
        });
    }

    /// <summary>Setting out after an unseen hostile is what takes the hunt for it; a plan that is never run does not.</summary>
    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        if (blackboard.TryGetValue<EntityUid>("Target", out var target, IoCManager.Resolve<IEntityManager>()))
            _weapons.ClaimAdvance(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), target);
    }

    /// <summary>Rejects targets lost while pathfinding before the following movement task starts.</summary>
    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return blackboard.ContainsKey("Target")
            && _weapons.CanEngage(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), blackboard.GetValue<EntityUid>("Target"))
            ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }
}
