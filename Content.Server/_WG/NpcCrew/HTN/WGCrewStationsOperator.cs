using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>At battle stations: a crewman who fights on sight draws his weapon, anyone else puts his away.</summary>
public sealed partial class WGCrewStationsOperator : HTNOperator
{
    private WGCrewWeaponSystem _weapons = default!;
    private WGCrewReadinessSystem _readiness = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _weapons = sysManager.GetEntitySystem<WGCrewWeaponSystem>();
        _readiness = sysManager.GetEntitySystem<WGCrewReadinessSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_readiness.StandsToArms(owner))
            _weapons.TryDraw(owner);
        else
            _weapons.TryHolster(owner);
        return HTNOperatorStatus.Finished;
    }
}
