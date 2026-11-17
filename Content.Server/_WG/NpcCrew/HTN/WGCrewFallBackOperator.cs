using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>Picks cover for a wounded crewman to fall back to. See <see cref="WGCrewFallBackSystem"/>.</summary>
public sealed partial class WGCrewFallBackOperator : HTNOperator
{
    /// <summary>The cover the crewman moves to.</summary>
    public const string CoverKey = "WGCrewFallBack";

    private WGCrewFallBackSystem _fallBack = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _fallBack = sysManager.GetEntitySystem<WGCrewFallBackSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_fallBack.TryFindCover(owner, out var cover))
            return (false, null);

        return (true, new Dictionary<string, object> { { CoverKey, cover } });
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        _fallBack.Begin(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }
}

/// <summary>Patches a crewman up in cover until he can rejoin the fight. See <see cref="WGCrewFallBackSystem"/>.</summary>
public sealed partial class WGCrewPatchOperator : HTNOperator
{
    private WGCrewFallBackSystem _fallBack = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _fallBack = sysManager.GetEntitySystem<WGCrewFallBackSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return _fallBack.Patch(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner))
            ? HTNOperatorStatus.Continuing
            : HTNOperatorStatus.Finished;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _fallBack.EndPatch(owner);
        blackboard.Remove<EntityCoordinates>(WGCrewFallBackOperator.CoverKey);
    }
}
