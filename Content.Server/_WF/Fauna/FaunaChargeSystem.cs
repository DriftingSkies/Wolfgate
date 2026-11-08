using System.Numerics;
using Content.Shared._WF.Fauna;
using Content.Shared.ActionBlocker;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Runs the charge of <see cref="FaunaChargeComponent"/>.
/// </summary>
public sealed partial class FaunaChargeSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private ThrownItemSystem _thrownItem = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedCameraRecoilSystem _cameraRecoil = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly HashSet<Entity<ActorComponent>> _players = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaChargeComponent, FaunaChargeActionEvent>(OnChargeAction);
        SubscribeLocalEvent<FaunaChargeComponent, ThrowDoHitEvent>(OnChargeHit);
        SubscribeLocalEvent<FaunaChargeComponent, StopThrowEvent>(OnChargeStop);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaChargeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.PendingTarget is not { } target || now < comp.ChargeAt)
                continue;

            comp.PendingTarget = null;
            _appearance.SetData(uid, FaunaChargeVisuals.Windup, Vector2.Zero);
            if (!TerminatingOrDeleted(target)
                && !_container.IsEntityInContainer(target)
                && _mobState.IsAlive(uid)
                && _actionBlocker.CanMove(uid))
            {
                Charge((uid, comp), target);
            }
        }
    }

    private void OnChargeAction(Entity<FaunaChargeComponent> ent, ref FaunaChargeActionEvent args)
    {
        if (args.Handled || ent.Comp.Charging || ent.Comp.PendingTarget != null)
            return;

        if (ent.Comp.Windup <= TimeSpan.Zero)
        {
            args.Handled = Charge(ent, args.Target);
            return;
        }

        args.Handled = true;
        ent.Comp.PendingTarget = args.Target;
        ent.Comp.ChargeAt = _timing.CurTime + ent.Comp.Windup;
        var away = _transform.GetWorldPosition(ent) - _transform.GetWorldPosition(args.Target);
        if (away.LengthSquared() > 0.01f)
            _appearance.SetData(ent, FaunaChargeVisuals.Windup, Vector2.Normalize(away));

        if (ent.Comp.WindupPopup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner)), ent, PopupType.MediumCaution);
    }

    private bool Charge(Entity<FaunaChargeComponent> ent, EntityUid target)
    {
        var origin = _transform.GetMapCoordinates(ent);
        var targetPos = _transform.GetMapCoordinates(target);
        if (origin.MapId != targetPos.MapId)
            return false;

        var diff = targetPos.Position - origin.Position;
        if (diff.LengthSquared() < 0.01f)
            return false;

        if (diff.Length() > ent.Comp.Range)
            diff = diff.Normalized() * ent.Comp.Range;

        diff += diff.Normalized() * ent.Comp.Overshoot;

        SetCharging(ent, true);
        _audio.PlayPvs(ent.Comp.ChargeSound, ent);
        if (ent.Comp.Shake > 0)
            KickCameras(ent, origin, diff);

        _throwing.TryThrow(ent, diff, ent.Comp.Speed, animated: false, playSound: false, doSpin: false);
        return true;
    }

    // Hit on collision, never on landing: a knockdown drops items and starts new throws.
    private void OnChargeHit(Entity<FaunaChargeComponent> ent, ref ThrowDoHitEvent args)
    {
        if (!ent.Comp.Charging)
            return;

        SetCharging(ent, false);
        _thrownItem.StopThrow(ent, args.Component);

        if (!HasComp<MobStateComponent>(args.Target))
        {
            if (ent.Comp.StructureDamage != null)
                _damageable.TryChangeDamage(args.Target, ent.Comp.StructureDamage, origin: ent);

            _stun.TryStun(ent, ent.Comp.BlockedStun, true);
            return;
        }

        if (!_mobState.IsAlive(args.Target))
            return;

        if (ent.Comp.Damage != null)
            _damageable.TryChangeDamage(args.Target, ent.Comp.Damage, origin: ent);

        _stun.TryKnockdown(args.Target, ent.Comp.Knockdown, true);
        var away = _transform.GetWorldPosition(args.Target) - _transform.GetWorldPosition(ent);
        if (ent.Comp.Throw > 0 && away.LengthSquared() > 0.01f)
        {
            var spread = Angle.FromDegrees(_random.NextFloat(-ent.Comp.ThrowSpread, ent.Comp.ThrowSpread));
            var direction = spread.RotateVec(Vector2.Normalize(away));
            _throwing.TryThrow(args.Target, direction * ent.Comp.Throw, 10f, ent, pushbackRatio: 0f);
        }

        var ev = new FaunaChargeHitEvent(args.Target);
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnChargeStop(Entity<FaunaChargeComponent> ent, ref StopThrowEvent args)
    {
        SetCharging(ent, false);
    }

    /// <summary>
    /// Kicks the cameras of players within charge range along the charge direction.
    /// </summary>
    private void KickCameras(Entity<FaunaChargeComponent> ent, MapCoordinates origin, Vector2 diff)
    {
        var kick = Vector2.Normalize(diff) * ent.Comp.Shake;
        _players.Clear();
        _lookup.GetEntitiesInRange(origin, ent.Comp.Range, _players);
        foreach (var player in _players)
        {
            if (player.Owner != ent.Owner)
                _cameraRecoil.KickCamera(player, kick);
        }
    }

    private void SetCharging(Entity<FaunaChargeComponent> ent, bool charging)
    {
        ent.Comp.Charging = charging;
        _appearance.SetData(ent, FaunaChargeVisuals.Charging, charging);
    }
}
