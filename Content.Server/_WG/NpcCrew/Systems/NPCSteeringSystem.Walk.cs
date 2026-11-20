using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.NPC.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Slippery;
using Content.Shared.StepTrigger.Components;

namespace Content.Server.NPC.Systems;

/// <summary>NPC crew out of a fight walk, rather than run, past a slippery puddle, so they don't slip in it.</summary>
public sealed partial class NPCSteeringSystem
{
    /// <summary>How near a slippery puddle must be for a crewman to slow to a walk.</summary>
    private const float CrewSlipRange = 2f;

    /// <summary>How long a crewman's look for puddles holds before he looks again.</summary>
    private static readonly TimeSpan CrewSlipRecheck = TimeSpan.FromSeconds(0.25);

    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Near)> _crewSlipNear = new();
    private readonly HashSet<Entity<SlipperyComponent>> _crewSlippery = new();

    /// <summary>Turns a crewman's run into a walk while a puddle he would slip in is near and he isn't fighting.</summary>
    private void CrewWalkNearSlip(EntityUid uid, InputMoverComponent mover)
    {
        if (!HasComp<WGCrewComponent>(uid))
            return;

        mover.CurTickWalkMovement = Vector2.Zero;
        if (mover.CurTickSprintMovement == Vector2.Zero || HasComp<NPCMeleeCombatComponent>(uid)
            || HasComp<NPCRangedCombatComponent>(uid) || !IsCrewSlipNear(uid))
            return;

        mover.CurTickWalkMovement = mover.CurTickSprintMovement;
        mover.CurTickSprintMovement = Vector2.Zero;
    }

    /// <summary>Whether a slippery thing that a running crewman would slip on, and a walking one would not, is near him.</summary>
    private bool IsCrewSlipNear(EntityUid uid)
    {
        var now = _timing.CurTime;
        if (_crewSlipNear.TryGetValue(uid, out var cached) && cached.Until > now)
            return cached.Near;

        var hasSpeed = TryComp<MovementSpeedModifierComponent>(uid, out var speed);
        var walk = hasSpeed ? speed!.CurrentWalkSpeed : MovementSpeedModifierComponent.DefaultBaseWalkSpeed;
        var run = hasSpeed ? speed!.CurrentSprintSpeed : MovementSpeedModifierComponent.DefaultBaseSprintSpeed;
        var near = false;
        _crewSlippery.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, CrewSlipRange, _crewSlippery);
        foreach (var slippery in _crewSlippery)
        {
            if (TryComp<StepTriggerComponent>(slippery, out var trigger) && trigger.Active
                && trigger.RequiredTriggeredSpeed > walk && trigger.RequiredTriggeredSpeed <= run)
            {
                near = true;
                break;
            }
        }

        // Entries of crew that died or left are dropped as the table grows.
        if (_crewSlipNear.Count > 256)
            _crewSlipNear.Clear();
        _crewSlipNear[uid] = (now + CrewSlipRecheck, near);
        return near;
    }
}
