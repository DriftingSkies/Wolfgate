using Content.Server._WG.NpcCrew.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks.Operators.Interactions;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>Keeps crewmen off each other's chores: a target another crewman's running plan is going to work on is taken.</summary>
public sealed class WGCrewClaimSystem : EntitySystem
{
    /// <summary>
    /// Whether a crewman other than <paramref name="owner"/> holds <paramref name="target"/> under a blackboard key in a
    /// plan he is still running that interacts with it, so a key left over from a plan he dropped (a break, battle
    /// stations) claims nothing.
    /// </summary>
    public bool ClaimedByOther(EntityUid owner, EntityUid target, string key)
    {
        var query = EntityQueryEnumerator<WGCrewComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out _, out var htn))
        {
            if (uid == owner || htn.Plan is not { } plan
                || !htn.Blackboard.TryGetValue<EntityUid>(key, out var held, EntityManager) || held != target)
                continue;
            for (var i = plan.Index; i < plan.Tasks.Count; i++)
            {
                if (plan.Tasks[i].Operator is InteractWithOperator interact && interact.TargetKey == key)
                    return true;
            }
        }
        return false;
    }
}
