using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.ActionBlocker;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;

    /// <summary>
    /// Whether an NPC is fit to fire: not crit, stunned or asleep, and not Downed. Players keep a sidearm while
    /// Downed; NPCs don't, or a pain-downed mob keeps shooting from the floor.
    /// </summary>
    public bool CanNpcShoot(EntityUid uid)
    {
        return !HasComp<WolfmedDownedComponent>(uid) && _blocker.CanAttack(uid);
    }

    /// <summary>Where a crewman's shot actually goes: the aim point, swung off by up to his skill's error.</summary>
    private EntityCoordinates CrewAim(EntityUid uid, EntityCoordinates target)
    {
        if (!TryComp<WFCrewComponent>(uid, out var crew))
            return target;

        var error = WFCrewSkills.Of(crew.Skill).AimError;
        if (error <= 0f)
            return target;

        var from = _transform.GetMapCoordinates(uid).Position;
        var aim = _transform.ToMapCoordinates(target).Position;
        var missed = from + Angle.FromDegrees(_random.NextFloat(-error, error)).RotateVec(aim - from);
        return _transform.ToCoordinates(target.EntityId, new MapCoordinates(missed, Transform(uid).MapID));
    }
}
