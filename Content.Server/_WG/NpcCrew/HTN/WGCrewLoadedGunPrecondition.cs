using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>
/// Opens the gunfight branch when the crewman has a loaded gun in hand or in a slot. The stock ammo check only
/// looks at the hand, which is empty until the weapon is drawn, and would plan every first fight as a charge.
/// </summary>
public sealed partial class WGCrewLoadedGunPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return _entManager.System<WGCrewWeaponSystem>().HasLoadedGun(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
