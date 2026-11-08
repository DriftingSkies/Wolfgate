using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Latches <see cref="FaunaLeechComponent"/> mobs onto humanoids they hit and feeds them until the latch breaks.
/// </summary>
public sealed partial class FaunaLeechSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const string BurnGroup = "Burn";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaLeechComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<FaunaLeechComponent, UpdateCanMoveEvent>(OnUpdateCanMove);
        SubscribeLocalEvent<FaunaLeechComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FaunaLeechComponent, EntityTerminatingEvent>(OnTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaLeechComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Victim is not { } victim)
                continue;

            Entity<FaunaLeechComponent> ent = (uid, comp);
            if (!CanHold(ent, victim, xform))
            {
                Release(ent);
                continue;
            }

            var victimCoords = Transform(victim).Coordinates;
            if (xform.Coordinates != victimCoords)
                _transform.SetCoordinates(uid, victimCoords);

            if (now < comp.NextTick)
                continue;

            comp.NextTick = now + comp.Interval;
            if (comp.Damage != null)
                _damageable.TryChangeDamage(victim, comp.Damage, origin: uid);

            if (comp.Heal != null)
                _damageable.TryChangeDamage(uid, comp.Heal, true, false);

            if (comp.PainPopup is { } pain && _random.Prob(comp.PainChance))
                _popup.PopupEntity(Loc.GetString(pain, ("user", uid)), victim, victim, PopupType.MediumCaution);
        }
    }

    private void OnMeleeHit(Entity<FaunaLeechComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.User != ent.Owner || args.HitEntities.Count == 0 || ent.Comp.Victim != null)
            return;

        var target = args.HitEntities[0];
        if (!HasComp<HumanoidAppearanceComponent>(target) || !_mobState.IsAlive(target) ||
            _container.IsEntityInContainer(target) || !_random.Prob(ent.Comp.Chance))
            return;

        ent.Comp.Victim = target;
        ent.Comp.NextTick = _timing.CurTime + ent.Comp.Interval;
        _transform.SetCoordinates(ent, Transform(target).Coordinates);
        _actionBlocker.UpdateCanMove(ent);

        if (ent.Comp.LatchPopup is { } popup)
        {
            var message = Loc.GetString(popup, ("user", ent.Owner), ("target", target));
            _popup.PopupEntity(message, target, PopupType.MediumCaution);
        }
    }

    private void OnUpdateCanMove(Entity<FaunaLeechComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.Victim != null)
            args.Cancel();
    }

    private void OnMobStateChanged(Entity<FaunaLeechComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Release(ent);
    }

    private void OnTerminating(Entity<FaunaLeechComponent> ent, ref EntityTerminatingEvent args)
    {
        ent.Comp.Victim = null;
    }

    /// <summary>
    /// Whether the latch still holds: both alive, side by side on one map, and the victim not burnt past the limit.
    /// </summary>
    private bool CanHold(Entity<FaunaLeechComponent> ent, EntityUid victim, TransformComponent xform)
    {
        if (TerminatingOrDeleted(victim) || _container.IsEntityInContainer(victim) ||
            !_mobState.IsAlive(ent) || _mobState.IsDead(victim))
            return false;

        var victimXform = Transform(victim);
        if (victimXform.MapID != xform.MapID ||
            (_transform.GetWorldPosition(victimXform) - _transform.GetWorldPosition(xform)).Length() > ent.Comp.Range)
            return false;

        if (!TryComp<DamageableComponent>(victim, out var damageable) ||
            !damageable.DamagePerGroup.TryGetValue(BurnGroup, out var burn) ||
            !_thresholds.TryGetDeadThreshold(victim, out var dead))
            return true;

        return burn <= dead.Value * ent.Comp.BurnLimit;
    }

    private void Release(Entity<FaunaLeechComponent> ent)
    {
        if (ent.Comp.Victim is not { } victim)
            return;

        ent.Comp.Victim = null;
        _actionBlocker.UpdateCanMove(ent);

        if (ent.Comp.ReleasePopup is { } popup && !TerminatingOrDeleted(victim))
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner), ("target", victim)), victim, PopupType.Small);
    }
}
