using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared._WG.NpcCrew;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>
/// Gates the fight branch by the crewman's engagement rule. On-sight crew may always fight (the combat compounds
/// still need a target); when-attacked crew only while they remember being attacked.
/// </summary>
public sealed partial class WGCrewMayFightPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_entManager.TryGetComponent<WGCrewComponent>(owner, out var crew) && crew.Engagement == WGCrewEngagement.Never)
            return false;

        var weapons = _entManager.System<WGCrewWeaponSystem>();
        if (!weapons.HasLiveThreat(owner))
            return false;

        return _entManager.System<WGCrewSecuritySystem>().HasThreat(owner) || weapons.Provoked(owner);
    }
}
