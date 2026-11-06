using Content.Server.Interaction;
using Content.Server.NPC.HTN;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Content.Shared.NPC.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Fires <see cref="FaunaAbilitiesComponent"/> actions at the HTN target of awake NPCs.
/// </summary>
public sealed partial class FaunaAbilitiesSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private InteractionSystem _interaction = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaAbilitiesComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FaunaAbilitiesComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnMapInit(Entity<FaunaAbilitiesComponent> ent, ref MapInitEvent args)
    {
        foreach (var ability in ent.Comp.Abilities)
        {
            ability.ActionEnt = _actions.AddAction(ent, ability.Action);
        }
    }

    private void OnRefreshSpeed(Entity<FaunaAbilitiesComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.HoldUntil > _timing.CurTime)
            args.ModifySpeed(0f, 0f);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaAbilitiesComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.HoldUntil != TimeSpan.Zero && comp.HoldUntil <= now)
            {
                comp.HoldUntil = TimeSpan.Zero;
                _speed.RefreshMovementSpeedModifiers(uid);
            }

            if (!HasComp<ActiveNPCComponent>(uid) || !TryComp<HTNComponent>(uid, out var htn))
                continue;

            if (now < comp.NextCheck || comp.HoldUntil > now)
                continue;

            comp.NextCheck = now + comp.Interval;

            if (!_mobState.IsAlive(uid) ||
                !htn.Blackboard.TryGetValue<EntityUid>(FaunaHelperSystem.TargetKey, out var target, EntityManager) ||
                TerminatingOrDeleted(target))
            {
                continue;
            }

            var targetAlive = _mobState.IsAlive(target);

            var origin = _transform.GetMapCoordinates(uid);
            var targetPos = _transform.GetMapCoordinates(target);
            if (origin.MapId != targetPos.MapId)
                continue;

            var distance = (targetPos.Position - origin.Position).Length();
            bool? inSight = null;

            foreach (var ability in comp.Abilities)
            {
                if (!targetAlive && !ability.Corpses)
                    continue;

                if (ability.BelowHealth > 0 && HealthLeft(uid) >= ability.BelowHealth)
                    continue;

                if (distance < ability.MinRange || distance > ability.MaxRange)
                    continue;

                inSight ??= _interaction.InRangeUnobstructed(uid, target, distance + 0.5f);
                if (inSight == false)
                    break;

                if (ability.MinHostiles > 0 && CountHostiles(uid, ability.MaxRange) < ability.MinHostiles)
                    continue;

                if (!TryUse(uid, ability, target))
                    continue;

                if (ability.Hold > TimeSpan.Zero)
                {
                    comp.HoldUntil = now + ability.Hold;
                    _speed.RefreshMovementSpeedModifiers(uid);
                }

                break;
            }
        }
    }

    private float HealthLeft(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable) ||
            !_thresholds.TryGetDeadThreshold(uid, out var dead) ||
            dead <= 0)
            return 1f;

        return 1f - (damageable.TotalDamage / dead.Value).Float();
    }

    private int CountHostiles(EntityUid uid, float range)
    {
        var count = 0;
        foreach (var hostile in _faction.GetNearbyHostiles(uid, range))
        {
            if (_mobState.IsAlive(hostile))
                count++;
        }

        return count;
    }

    private bool TryUse(EntityUid uid, FaunaAbility ability, EntityUid target)
    {
        if (!_actions.TryGetActionData(ability.ActionEnt, out var action, false) || !_actions.ValidAction(action))
            return false;

        var coords = Transform(target).Coordinates;
        BaseActionEvent? ev;
        switch (action)
        {
            case InstantActionComponent instant:
                ev = instant.Event;
                break;
            case EntityTargetActionComponent entityTarget:
                if (entityTarget.Event != null)
                    entityTarget.Event.Target = target;
                ev = entityTarget.Event;
                break;
            case WorldTargetActionComponent worldTarget:
                if (worldTarget.Event != null)
                    worldTarget.Event.Target = coords;
                ev = worldTarget.Event;
                break;
            case EntityWorldTargetActionComponent entityWorldTarget:
                if (entityWorldTarget.Event != null)
                {
                    entityWorldTarget.Event.Entity = target;
                    entityWorldTarget.Event.Coords = coords;
                }
                ev = entityWorldTarget.Event;
                break;
            default:
                return false;
        }

        if (ev == null)
            return false;

        _actions.PerformAction(uid, null, ability.ActionEnt!.Value, action, ev, _timing.CurTime, false);
        return ev.Handled;
    }
}
