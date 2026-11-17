using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._WG.Wolfmed.Consciousness;
using Content.Server.NPC.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    /// <summary>Seconds between a crewman's semi-automatic shots. A gun's fire rate is only its upper limit.</summary>
    public const float CrewSemiAutoInterval = 0.5f;

    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private WGCrewWeaponSystem _wgCrewWeapons = default!;

    /// <summary>
    /// A crewman with his target in sight. Regaining sight of one he saw moments ago or was braced for, he fires after
    /// his skill's short re-acquire delay instead of the full first-contact one.
    /// </summary>
    private void CrewSighted(EntityUid uid, NPCRangedCombatComponent combat, bool regained)
    {
        if (!TryComp<WGCrewWeaponComponent>(uid, out var weapon) || !TryComp<WGCrewComponent>(uid, out var crew))
            return;

        if (regained && _wgCrewWeapons.Expects((uid, weapon), combat.Target))
        {
            var ready = combat.ShootDelay - WGCrewSkills.Of(crew).ReacquireDelay;
            combat.ShootAccumulator = MathF.Max(combat.ShootAccumulator, ready);
        }

        _wgCrewWeapons.Saw((uid, weapon), combat.Target);
    }

    /// <summary>
    /// Whether an NPC is fit to fire: not crit, stunned or asleep, and not Downed. Players keep a sidearm while
    /// Downed; NPCs don't, or a pain-downed mob keeps shooting from the floor.
    /// </summary>
    public bool CanNpcShoot(EntityUid uid)
    {
        return !HasComp<WolfmedDownedComponent>(uid) && _blocker.CanAttack(uid);
    }

    /// <summary>Holds a crewman's next semi-automatic shot back, so a pistol doesn't fire at its full rate like a machine gun.</summary>
    public void PaceCrewFire(EntityUid uid, GunComponent gun, NPCRangedCombatComponent combat)
    {
        if (gun.SelectedMode == SelectiveFire.SemiAuto && HasComp<WGCrewComponent>(uid))
            combat.ShootAccumulator = combat.ShootDelay - CrewSemiAutoInterval;
    }

    /// <summary>Where a crewman's shot actually goes: the aim point, swung off by up to his skill's error.</summary>
    private EntityCoordinates CrewAim(EntityUid uid, EntityCoordinates target)
    {
        if (!TryComp<WGCrewComponent>(uid, out var crew))
            return target;

        var error = WGCrewSkills.Of(crew).AimError;
        if (error <= 0f)
            return target;

        var from = _transform.GetMapCoordinates(uid).Position;
        var aim = _transform.ToMapCoordinates(target).Position;
        var missed = from + Angle.FromDegrees(_random.NextFloat(-error, error)).RotateVec(aim - from);
        return _transform.ToCoordinates(target.EntityId, new MapCoordinates(missed, Transform(uid).MapID));
    }
}
